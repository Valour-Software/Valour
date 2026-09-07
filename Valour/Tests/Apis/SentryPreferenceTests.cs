using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server.Api.Dynamic;
using Valour.Server.Database;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

[Collection("ApiCollection")]
public class SentryPreferenceTests(LoginTestFixture fixture)
{
    [Fact]
    public async Task ConcurrentFirstWrites_PreserveUnrelatedPreferences()
    {
        var userId = fixture.Client.Me.Id;
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var original = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        await db.UserPreferences.Where(x => x.Id == userId).ExecuteDeleteAsync();
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                using var writeScope = fixture.Factory.Services.CreateScope();
                var writeDb = writeScope.ServiceProvider.GetRequiredService<ValourDb>();
                Assert.NotNull(await UserApi.SetErrorReportingStateAsync(userId, ErrorReportingState.Unset, writeDb));
            }));
            Assert.Equal(1, await db.UserPreferences.CountAsync(x => x.Id == userId));
            await db.UserPreferences.Where(x => x.Id == userId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.NotificationVolume, 17)
                .SetProperty(x => x.MarketingEmailOptOut, true));
            var updated = await UserApi.SetErrorReportingStateAsync(userId, ErrorReportingState.All, db);
            Assert.NotNull(updated);
            Assert.Equal(ErrorReportingState.All, updated.ErrorReportingState);
            Assert.Equal(17, updated.NotificationVolume);
            Assert.True(updated.MarketingEmailOptOut);
        }
        finally
        {
            await db.UserPreferences.Where(x => x.Id == userId).ExecuteDeleteAsync();
            if (original is not null)
            {
                db.UserPreferences.Add(original);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task MissingUser_DoesNotCreatePreferences()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var missingId = IdManager.Generate();
        Assert.Null(await UserApi.SetErrorReportingStateAsync(missingId, ErrorReportingState.Unset, db));
        Assert.False(await db.UserPreferences.AnyAsync(x => x.Id == missingId));
    }
}
