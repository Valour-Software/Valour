using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class DirectCallServiceTests : IDisposable
{
    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly ChannelService _channelService;
    private readonly DirectCallService _callService;

    public DirectCallServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _channelService = _scope.ServiceProvider.GetRequiredService<ChannelService>();
        _callService = _scope.ServiceProvider.GetRequiredService<DirectCallService>();
    }

    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task DirectCall_RingsAcceptsAndEndsWhenOnlyOneParticipantRemains()
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var callerId = _fixture.Client.Me.Id;
        var targetId = await _db.Users.Where(x => x.Id != callerId).Select(x => x.Id).FirstAsync();

        var preferences = await _db.UserPreferences.FindAsync(targetId);
        if (preferences is null)
        {
            preferences = new Valour.Database.UserPreferences { Id = targetId };
            _db.UserPreferences.Add(preferences);
            await _db.SaveChangesAsync();
        }
        preferences.CallPolicy = DmPolicy.Everyone;
        await _db.SaveChangesAsync();

        var channel = await _channelService.GetDirectChannelByUsersAsync(callerId, targetId, create: true);
        Assert.NotNull(channel);
        Assert.All(channel.Members.Where(x => x.UserId != callerId), x => Assert.Equal(targetId, x.UserId));
        Assert.Equal(
            DmPolicy.Everyone,
            await _db.UserPreferences.AsNoTracking()
                .Where(x => x.Id == targetId)
                .Select(x => x.CallPolicy)
                .SingleAsync());

        var started = await _callService.StartAsync(callerId, new StartDirectCallRequest
        {
            ChannelId = channel.Id,
            Kind = DirectCallKind.Audio
        });
        Assert.True(started.Success, started.Message);
        Assert.Equal(DirectCallState.Ringing, started.Data.State);
        Assert.Contains(started.Data.Members, x => x.UserId == targetId && x.State == DirectCallMemberState.Invited);

        var accepted = await _callService.AcceptAsync(started.Data.Id, targetId);
        Assert.True(accepted.Success, accepted.Message);
        Assert.Equal(DirectCallState.Active, accepted.Data.State);

        var left = await _callService.LeaveAsync(started.Data.Id, targetId, null);
        Assert.True(left.Success, left.Message);
        Assert.Equal(DirectCallState.Ended, left.Data.State);
        Assert.Equal(DirectCallEndReason.Completed, left.Data.EndReason);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Start_RejectsCallerWhoIsNotAConversationMember()
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var userIds = await GetAvailableUserIdsAsync(2);
        var channel = await _channelService.GetDirectChannelByUsersAsync(userIds[0], userIds[1], create: true);
        Assert.NotNull(channel);

        var result = await _callService.StartAsync(_fixture.Client.Me.Id, new StartDirectCallRequest
        {
            ChannelId = channel.Id,
            Kind = DirectCallKind.Audio
        });

        Assert.False(result.Success);
        Assert.Contains("member", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await _db.DirectCalls.AnyAsync(x => x.ChannelId == channel.Id));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Start_RespectsFriendsOnlyCallPrivacy()
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var callerId = _fixture.Client.Me.Id;
        var targetId = (await GetAvailableUserIdsAsync(1))[0];
        await SetCallPolicyAsync(targetId, DmPolicy.FriendsOnly);
        await _db.UserFriends
            .Where(x => (x.UserId == callerId && x.FriendId == targetId) ||
                        (x.UserId == targetId && x.FriendId == callerId))
            .ExecuteDeleteAsync();

        var channel = await _channelService.GetDirectChannelByUsersAsync(callerId, targetId, create: true);
        Assert.NotNull(channel);

        var result = await _callService.StartAsync(callerId, new StartDirectCallRequest
        {
            ChannelId = channel.Id,
            Kind = DirectCallKind.Video
        });

        Assert.False(result.Success);
        Assert.Contains("friends", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await _db.DirectCalls.AnyAsync(x => x.ChannelId == channel.Id));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Start_RejectsASecondCallForBusyParticipants()
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var callerId = _fixture.Client.Me.Id;
        var targetId = (await GetAvailableUserIdsAsync(1))[0];
        await SetCallPolicyAsync(targetId, DmPolicy.Everyone);
        var channel = await _channelService.GetDirectChannelByUsersAsync(callerId, targetId, create: true);
        Assert.NotNull(channel);

        var first = await _callService.StartAsync(callerId, new StartDirectCallRequest
        {
            ChannelId = channel.Id,
            Kind = DirectCallKind.Audio
        });
        Assert.True(first.Success, first.Message);

        var second = await _callService.StartAsync(callerId, new StartDirectCallRequest
        {
            ChannelId = channel.Id,
            Kind = DirectCallKind.Video
        });

        Assert.False(second.Success);
        Assert.Contains("another call", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await _db.DirectCalls.Where(x => x.ChannelId == channel.Id).ToListAsync());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ExpiredRingingCall_IsRecordedAsMissed()
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var callerId = _fixture.Client.Me.Id;
        var targetId = (await GetAvailableUserIdsAsync(1))[0];
        await SetCallPolicyAsync(targetId, DmPolicy.Everyone);
        var channel = await _channelService.GetDirectChannelByUsersAsync(callerId, targetId, create: true);
        Assert.NotNull(channel);

        var started = await _callService.StartAsync(callerId, new StartDirectCallRequest
        {
            ChannelId = channel.Id,
            Kind = DirectCallKind.Audio
        });
        Assert.True(started.Success, started.Message);

        var dbCall = await _db.DirectCalls.SingleAsync(x => x.Id == started.Data.Id);
        dbCall.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync();

        var expiredCount = await _callService.ExpireRingingCallsAsync();
        await _db.Entry(dbCall).ReloadAsync();

        Assert.True(expiredCount >= 1);
        Assert.Equal(DirectCallState.Ended, dbCall.State);
        Assert.Equal(DirectCallEndReason.Missed, dbCall.EndReason);
        Assert.NotNull(dbCall.EndedAt);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AddParticipant_ConvertsDirectCallToFreshGroupAndInvitesNewMember()
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var callerId = _fixture.Client.Me.Id;
        var targetIds = await GetAvailableUserIdsAsync(2);
        await SetCallPolicyAsync(targetIds[0], DmPolicy.Everyone);
        await SetCallPolicyAsync(targetIds[1], DmPolicy.Everyone);
        var direct = await _channelService.GetDirectChannelByUsersAsync(callerId, targetIds[0], create: true);
        Assert.NotNull(direct);

        var started = await _callService.StartAsync(callerId, new StartDirectCallRequest
        {
            ChannelId = direct.Id,
            Kind = DirectCallKind.Video
        });
        Assert.True(started.Success, started.Message);
        var accepted = await _callService.AcceptAsync(started.Data.Id, targetIds[0]);
        Assert.True(accepted.Success, accepted.Message);

        var expanded = await _callService.AddParticipantsAsync(
            started.Data.Id,
            callerId,
            new AddDirectCallParticipantsRequest { UserIds = [targetIds[1]] });

        Assert.True(expanded.Success, expanded.Message);
        Assert.NotEqual(direct.Id, expanded.Data.ChannelId);
        Assert.Equal(DirectCallState.Active, expanded.Data.State);
        Assert.Contains(expanded.Data.Members, x =>
            x.UserId == targetIds[1] && x.State == DirectCallMemberState.Invited);

        var group = await _db.Channels.AsNoTracking()
            .Include(x => x.Members)
            .SingleAsync(x => x.Id == expanded.Data.ChannelId);
        Assert.Equal(ChannelTypeEnum.GroupChat, group.ChannelType);
        Assert.Equal(3, group.Members.Count);
        Assert.Contains(group.Members, x => x.UserId == callerId && x.IsAdmin);

        var original = await _db.Channels.AsNoTracking()
            .Include(x => x.Members)
            .SingleAsync(x => x.Id == direct.Id);
        Assert.Equal(ChannelTypeEnum.DirectChat, original.ChannelType);
        Assert.DoesNotContain(original.Members, x => x.UserId == targetIds[1]);
        await transaction.RollbackAsync();
    }

    private async Task<List<long>> GetAvailableUserIdsAsync(int count)
    {
        var callerId = _fixture.Client.Me.Id;
        var ids = await _db.Users.AsNoTracking()
            .Where(user => user.Id != callerId && !_db.DirectCallMembers.Any(member =>
                member.UserId == user.Id &&
                member.Call.State != DirectCallState.Ended &&
                (member.State == DirectCallMemberState.Invited ||
                 member.State == DirectCallMemberState.Joined)))
            .Select(x => x.Id)
            .Take(count)
            .ToListAsync();
        Assert.Equal(count, ids.Count);
        return ids;
    }

    private async Task SetCallPolicyAsync(long userId, DmPolicy policy)
    {
        var preferences = await _db.UserPreferences.FindAsync(userId);
        if (preferences is null)
        {
            preferences = new Valour.Database.UserPreferences { Id = userId };
            _db.UserPreferences.Add(preferences);
        }

        preferences.CallPolicy = policy;
        await _db.SaveChangesAsync();
    }
}
