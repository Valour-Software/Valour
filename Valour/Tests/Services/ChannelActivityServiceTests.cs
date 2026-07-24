using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Server.Workers;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class ChannelActivityServiceTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ValourClient _client;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly ChannelActivityService _activityService;
    private readonly NotificationService _notificationService;
    private readonly UnreadService _unreadService;
    private readonly PlanetMemberService _memberService;

    private readonly long _valourCentralId = ISharedPlanet.ValourCentralId;

    public ChannelActivityServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _factory = fixture.Factory;
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _activityService = _scope.ServiceProvider.GetRequiredService<ChannelActivityService>();
        _notificationService = _scope.ServiceProvider.GetRequiredService<NotificationService>();
        _unreadService = _scope.ServiceProvider.GetRequiredService<UnreadService>();
        _memberService = _scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<PlanetMember> RegisterMemberAsync()
    {
        var details = await _fixture.RegisterUser();
        var user = await _db.Users.FirstAsync(x => x.Name == details.Username);

        // New users are auto-joined to Valour Central
        var member = await _memberService.GetByUserAsync(user.Id, _valourCentralId);
        Assert.NotNull(member);
        return member;
    }

    private async Task<Channel> GetDefaultChannelAsync() =>
        await _db.Channels
            .Where(x => x.PlanetId == _valourCentralId && x.IsDefault)
            .Select(x => x.ToModel())
            .FirstAsync();

    private ChannelActivityEvaluation MakeEvaluation(Channel channel, bool conversationStart = true) =>
        new()
        {
            ChannelId = channel.Id,
            PlanetId = _valourCentralId,
            TriggerMessageId = IdManager.Generate(),
            WindowMessageCount = 5,
            WindowAuthorCount = 2,
            WindowAuthorUserIds = [_client.Me.Id],
            ConversationStart = conversationStart,
        };

    [Fact]
    public async Task Evaluate_NotifiesRecentViewer()
    {
        var channel = await GetDefaultChannelAsync();
        var viewer = await RegisterMemberAsync();

        var stateResult = await _unreadService.UpdateReadState(
            channel.Id, viewer.UserId, _valourCentralId, viewer.Id, DateTime.UtcNow);
        Assert.True(stateResult.Success, stateResult.Message);

        await _activityService.EvaluateAsync(MakeEvaluation(channel));

        var notification = await _db.Notifications.AsNoTracking().FirstOrDefaultAsync(x =>
            x.UserId == viewer.UserId
            && x.ChannelId == channel.Id
            && x.Source == NotificationSource.ChannelActivity);

        Assert.NotNull(notification);
        Assert.Null(notification.TimeRead);
        Assert.Contains("picking up", notification.Title);
    }

    [Fact]
    public async Task Evaluate_CooldownSuppressesRepeat()
    {
        var channel = await GetDefaultChannelAsync();
        var viewer = await RegisterMemberAsync();

        await _unreadService.UpdateReadState(
            channel.Id, viewer.UserId, _valourCentralId, viewer.Id, DateTime.UtcNow);

        await _activityService.EvaluateAsync(MakeEvaluation(channel));
        await _activityService.EvaluateAsync(MakeEvaluation(channel, conversationStart: false));

        var count = await _db.Notifications.AsNoTracking().CountAsync(x =>
            x.UserId == viewer.UserId
            && x.ChannelId == channel.Id
            && x.Source == NotificationSource.ChannelActivity);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Evaluate_RespectsPerChannelMute()
    {
        var channel = await GetDefaultChannelAsync();
        var viewer = await RegisterMemberAsync();

        await _unreadService.UpdateReadState(
            channel.Id, viewer.UserId, _valourCentralId, viewer.Id, DateTime.UtcNow);

        var muteResult = await _activityService.SetActivityAlertsAsync(
            channel.Id, viewer.UserId, _valourCentralId, viewer.Id, ChannelActivityAlerts.Off);
        Assert.True(muteResult.Success, muteResult.Message);
        Assert.Equal(ChannelActivityAlerts.Off, muteResult.Data.ActivityAlerts);

        // Muting must not clobber the view time
        Assert.NotEqual(DateTime.UnixEpoch, muteResult.Data.LastViewedTime);

        await _activityService.EvaluateAsync(MakeEvaluation(channel));

        Assert.False(await _db.Notifications.AnyAsync(x =>
            x.UserId == viewer.UserId
            && x.ChannelId == channel.Id
            && x.Source == NotificationSource.ChannelActivity));
    }

    [Fact]
    public async Task Evaluate_SkipsBurstAuthors()
    {
        var channel = await GetDefaultChannelAsync();
        var viewer = await RegisterMemberAsync();

        await _unreadService.UpdateReadState(
            channel.Id, viewer.UserId, _valourCentralId, viewer.Id, DateTime.UtcNow);

        var eval = MakeEvaluation(channel);
        eval.WindowAuthorUserIds = [viewer.UserId];

        await _activityService.EvaluateAsync(eval);

        Assert.False(await _db.Notifications.AnyAsync(x =>
            x.UserId == viewer.UserId
            && x.ChannelId == channel.Id
            && x.Source == NotificationSource.ChannelActivity));
    }

    [Fact]
    public async Task Evaluate_NotifiesFavoriteWithoutViewHistory()
    {
        var channel = await GetDefaultChannelAsync();
        var favoriter = await RegisterMemberAsync();

        await _db.ChannelFavorites.AddAsync(new Valour.Database.ChannelFavorite()
        {
            Id = IdManager.Generate(),
            UserId = favoriter.UserId,
            ChannelId = channel.Id,
            PlanetId = _valourCentralId,
            Position = 0,
        });
        await _db.SaveChangesAsync();

        await _activityService.EvaluateAsync(MakeEvaluation(channel));

        Assert.True(await _db.Notifications.AnyAsync(x =>
            x.UserId == favoriter.UserId
            && x.ChannelId == channel.Id
            && x.Source == NotificationSource.ChannelActivity));
    }

    [Fact]
    public async Task Evaluate_PlanetCadenceOff_SuppressesByDefault()
    {
        var channel = await GetDefaultChannelAsync();
        var viewer = await RegisterMemberAsync();

        await _unreadService.UpdateReadState(
            channel.Id, viewer.UserId, _valourCentralId, viewer.Id, DateTime.UtcNow);

        var dbPlanet = await _db.Planets.FirstAsync(x => x.Id == _valourCentralId);
        var originalCadence = dbPlanet.ActivityNotificationCadence;
        dbPlanet.ActivityNotificationCadence = ChannelActivityCadence.Off;
        await _db.SaveChangesAsync();

        try
        {
            await _activityService.EvaluateAsync(MakeEvaluation(channel));

            Assert.False(await _db.Notifications.AnyAsync(x =>
                x.UserId == viewer.UserId
                && x.ChannelId == channel.Id
                && x.Source == NotificationSource.ChannelActivity));

            // A personal cooldown preference overrides the planet's Off default
            await _db.UserPreferences.AddAsync(new Valour.Database.UserPreferences()
            {
                Id = viewer.UserId,
                ActivityCooldownSeconds = 300,
            });
            await _db.SaveChangesAsync();

            await _activityService.EvaluateAsync(MakeEvaluation(channel));

            Assert.True(await _db.Notifications.AnyAsync(x =>
                x.UserId == viewer.UserId
                && x.ChannelId == channel.Id
                && x.Source == NotificationSource.ChannelActivity));
        }
        finally
        {
            dbPlanet.ActivityNotificationCadence = originalCadence;
            await _db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SendChannelActivityNotifications_CoalescesPerChannel()
    {
        var channel = await GetDefaultChannelAsync();
        var viewer = await RegisterMemberAsync();

        var template = new Notification()
        {
            Title = "#general is picking up",
            Body = "3 messages from 2 people",
            PlanetId = _valourCentralId,
            ChannelId = channel.Id,
            Source = NotificationSource.ChannelActivity,
            SourceId = IdManager.Generate(),
        };

        await _notificationService.SendChannelActivityNotificationsAsync([viewer.UserId], template);

        template.Title = "#general is active";
        template.Body = "14 messages from 4 people";
        await _notificationService.SendChannelActivityNotificationsAsync([viewer.UserId], template);

        var rows = await _db.Notifications.AsNoTracking().Where(x =>
                x.UserId == viewer.UserId
                && x.ChannelId == channel.Id
                && x.Source == NotificationSource.ChannelActivity)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal("14 messages from 4 people", row.Body);
        Assert.Contains("is active", row.Title);
    }

    [Fact]
    public async Task HandleChannelViewed_MarksActivityNotificationRead()
    {
        var channel = await GetDefaultChannelAsync();
        var viewer = await RegisterMemberAsync();

        await _unreadService.UpdateReadState(
            channel.Id, viewer.UserId, _valourCentralId, viewer.Id, DateTime.UtcNow);

        await _activityService.EvaluateAsync(MakeEvaluation(channel));

        Assert.True(await _db.Notifications.AnyAsync(x =>
            x.UserId == viewer.UserId
            && x.ChannelId == channel.Id
            && x.Source == NotificationSource.ChannelActivity
            && x.TimeRead == null));

        // Viewing the channel again (read-state update) clears the entry
        await _unreadService.UpdateReadState(
            channel.Id, viewer.UserId, _valourCentralId, viewer.Id, DateTime.UtcNow);

        Assert.False(await _db.Notifications.AnyAsync(x =>
            x.UserId == viewer.UserId
            && x.ChannelId == channel.Id
            && x.Source == NotificationSource.ChannelActivity
            && x.TimeRead == null));
    }
}
