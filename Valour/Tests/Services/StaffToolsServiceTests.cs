using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Services;
using Valour.Shared.Models.Staff;

namespace Valour.Tests.Services;

/// <summary>
/// Staff support tooling: PII lookup, username resets, prior-name hiding,
/// delayed MFA removal, and the audit trail every action writes.
/// </summary>
[Collection("ApiCollection")]
public class StaffToolsServiceTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly StaffService _staff;

    private long _staffUserId;

    public StaffToolsServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _staff = _scope.ServiceProvider.GetRequiredService<StaffService>();
    }

    public ValueTask InitializeAsync()
    {
        _staffUserId = _fixture.Client.Me.Id;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Valour.Database.User> RegisterTargetAsync()
    {
        var details = await _fixture.RegisterUser();
        return await _db.Users.FirstAsync(x => x.Name == details.Username);
    }

    [Fact]
    public async Task Lookup_ReturnsEmail_AndWritesAuditRow()
    {
        var target = await RegisterTargetAsync();

        var result = await _staff.LookupUserAsync($"{target.Name}#{target.Tag}", _staffUserId, "test lookup");
        Assert.True(result.Success, result.Message);
        Assert.Equal(target.Id, result.Data.UserId);
        Assert.False(string.IsNullOrWhiteSpace(result.Data.Email));

        var audit = await _db.StaffAuditLogs
            .Where(x => x.TargetUserId == target.Id && x.ActionType == StaffActionType.LookupUser)
            .FirstOrDefaultAsync();
        Assert.NotNull(audit);
        Assert.Equal("test lookup", audit.Reason);
        Assert.Equal(_staffUserId, audit.StaffUserId);
    }

    [Fact]
    public async Task Lookup_ResolvesByRawUserId()
    {
        var target = await RegisterTargetAsync();

        var result = await _staff.LookupUserAsync(target.Id.ToString(), _staffUserId, "id lookup");
        Assert.True(result.Success, result.Message);
        Assert.Equal(target.Id, result.Data.UserId);
    }

    [Fact]
    public async Task ResetUsername_AssignsPlaceholder_HidesPriorName()
    {
        var target = await RegisterTargetAsync();
        var oldName = target.Name;

        var result = await _staff.ResetUsernameAsync(target.Id, _staffUserId, "rule violation");
        Assert.True(result.Success, result.Message);
        Assert.StartsWith("Valournaut", result.Data);

        var updated = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == target.Id);
        Assert.Equal(result.Data, updated.Name);
        Assert.Equal(oldName, updated.PriorName);
        Assert.True(updated.HidePriorName);

        // The mapper must not leak a hidden prior name to clients
        var model = updated.ToModel();
        Assert.Null(model.PriorName);
        Assert.Null(model.NameChangeTime);

        Assert.True(await _db.StaffAuditLogs.AnyAsync(x =>
            x.TargetUserId == target.Id && x.ActionType == StaffActionType.ResetUsername));
    }

    [Fact]
    public async Task PriorNameHidden_TogglesAndGatesMapper()
    {
        var target = await RegisterTargetAsync();

        target.PriorName = "OldName";
        target.NameChangeTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var hide = await _staff.SetPriorNameHiddenAsync(target.Id, true, _staffUserId, "privacy request");
        Assert.True(hide.Success, hide.Message);

        var hidden = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == target.Id);
        Assert.Null(hidden.ToModel().PriorName);

        var show = await _staff.SetPriorNameHiddenAsync(target.Id, false, _staffUserId, "user request");
        Assert.True(show.Success, show.Message);

        var visible = await _db.Users.AsNoTracking().FirstAsync(x => x.Id == target.Id);
        Assert.Equal("OldName", visible.ToModel().PriorName);
    }

    [Fact]
    public async Task MfaRemoval_RequiresMfa_DelaysAndExecutes()
    {
        var target = await RegisterTargetAsync();

        // No MFA -> cannot schedule
        var noMfa = await _staff.ScheduleMfaRemovalAsync(target.Id, _staffUserId, "support ticket");
        Assert.False(noMfa.Success);

        // Give the target a verified MFA method
        await _db.MultiAuths.AddAsync(new Valour.Database.MultiAuth
        {
            Id = IdManager.Generate(),
            UserId = target.Id,
            Type = "totp",
            Secret = "test-secret",
            Verified = true,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var scheduled = await _staff.ScheduleMfaRemovalAsync(target.Id, _staffUserId, "support ticket");
        Assert.True(scheduled.Success, scheduled.Message);

        // Double-scheduling is rejected
        var duplicate = await _staff.ScheduleMfaRemovalAsync(target.Id, _staffUserId, "again");
        Assert.False(duplicate.Success);

        var pending = await _db.PendingMfaRemovals.FirstAsync(x =>
            x.TargetUserId == target.Id && x.Status == Valour.Database.MfaRemovalStatus.Pending);
        Assert.True(pending.ExecuteAt > DateTime.UtcNow.AddHours(47));

        // Not due yet: the worker pass must not execute it
        await _staff.ExecutePendingMfaRemovalsAsync();
        Assert.True(await _db.MultiAuths.AnyAsync(x => x.UserId == target.Id));

        // Force it due and execute
        pending.ExecuteAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        var executed = await _staff.ExecutePendingMfaRemovalsAsync();
        Assert.True(executed >= 1);
        Assert.False(await _db.MultiAuths.AnyAsync(x => x.UserId == target.Id));

        var resolved = await _db.PendingMfaRemovals.AsNoTracking().FirstAsync(x => x.Id == pending.Id);
        Assert.Equal(Valour.Database.MfaRemovalStatus.Executed, resolved.Status);

        Assert.True(await _db.StaffAuditLogs.AnyAsync(x =>
            x.TargetUserId == target.Id && x.ActionType == StaffActionType.ExecuteMfaRemoval));
    }

    [Fact]
    public async Task MfaRemoval_OwnerCanCancel()
    {
        var target = await RegisterTargetAsync();

        await _db.MultiAuths.AddAsync(new Valour.Database.MultiAuth
        {
            Id = IdManager.Generate(),
            UserId = target.Id,
            Type = "totp",
            Secret = "test-secret",
            Verified = true,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var scheduled = await _staff.ScheduleMfaRemovalAsync(target.Id, _staffUserId, "support ticket");
        Assert.True(scheduled.Success, scheduled.Message);

        // The account owner cancels their own pending removal
        var cancel = await _staff.CancelMfaRemovalAsync(target.Id, target.Id, isStaff: false, "not me");
        Assert.True(cancel.Success, cancel.Message);

        Assert.False(await _db.PendingMfaRemovals.AnyAsync(x =>
            x.TargetUserId == target.Id && x.Status == Valour.Database.MfaRemovalStatus.Pending));

        // MFA methods are untouched
        Assert.True(await _db.MultiAuths.AnyAsync(x => x.UserId == target.Id && x.Verified));
    }

    [Fact]
    public async Task DeleteUser_RefusesStaffTargets()
    {
        var target = await RegisterTargetAsync();
        target.ValourStaff = true;
        await _db.SaveChangesAsync();

        var result = await _staff.DeleteUserAsync(target.Id, _staffUserId, "should fail");
        Assert.False(result.Success);
        Assert.NotNull(await _db.Users.FindAsync(target.Id));
    }

    [Fact]
    public async Task DisableUser_WritesAuditRow()
    {
        var target = await RegisterTargetAsync();

        var disable = await _staff.DisableUserAsync(target.Id, true, _staffUserId, "spam");
        Assert.True(disable.Success, disable.Message);

        Assert.True(await _db.StaffAuditLogs.AnyAsync(x =>
            x.TargetUserId == target.Id && x.ActionType == StaffActionType.DisableAccount && x.Reason == "spam"));

        var enable = await _staff.DisableUserAsync(target.Id, false, _staffUserId, "appeal accepted");
        Assert.True(enable.Success, enable.Message);

        Assert.True(await _db.StaffAuditLogs.AnyAsync(x =>
            x.TargetUserId == target.Id && x.ActionType == StaffActionType.EnableAccount));
    }
}
