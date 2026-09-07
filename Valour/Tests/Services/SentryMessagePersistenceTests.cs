using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Server.Workers;
using Valour.Server.Mapping;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class SentryMessagePersistenceTests(LoginTestFixture fixture)
{
    [Theory]
    [InlineData(null)]
    [InlineData("Valid staged message")]
    public async Task InvalidMessageInBatch_DoesNotPreventValidMessagePersistenceOrCountFailureAsSaved(string? content)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var channelId = await db.Channels.Where(x => x.PlanetId == ISharedPlanet.ValourCentralId &&
                x.IsDefault && x.ChannelType == ChannelTypeEnum.PlanetChat).Select(x => x.Id).FirstAsync();
        var valid = new Valour.Server.Models.Message
        {
            Id = IdManager.Generate(), ChannelId = channelId, PlanetId = ISharedPlanet.ValourCentralId,
            AuthorUserId = fixture.Client.Me.Id, Content = content, TimeSent = DateTime.UtcNow
        }.ToDatabase();
        var invalid = new Valour.Database.Message
        {
            Id = IdManager.Generate(), ChannelId = channelId, PlanetId = ISharedPlanet.ValourCentralId,
            AuthorUserId = long.MinValue, Content = "Missing author", TimeSent = DateTime.UtcNow
        };
        try
        {
            var saved = await PlanetMessageWorker.PersistMessagesAsync(db, [invalid, valid], NullLogger.Instance);
            Assert.Equal(valid.Id, Assert.Single(saved));
            Assert.True(await db.Messages.AnyAsync(x => x.Id == valid.Id));
            Assert.False(await db.Messages.AnyAsync(x => x.Id == invalid.Id));

            // A retried flush after a lost acknowledgement must recognize the existing row.
            var retried = await PlanetMessageWorker.PersistMessagesAsync(db, [valid], NullLogger.Instance);
            Assert.Equal(valid.Id, Assert.Single(retried));
            Assert.Equal(1, await db.Messages.CountAsync(x => x.Id == valid.Id));
        }
        finally
        {
            await db.Messages.IgnoreQueryFilters().Where(x => x.Id == valid.Id || x.Id == invalid.Id).ExecuteDeleteAsync();
        }
    }
}
