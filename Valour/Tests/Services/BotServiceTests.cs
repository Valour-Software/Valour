using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DbModerationAuditLog = Valour.Database.ModerationAuditLog;
using DbPlanetEmoji = Valour.Database.PlanetEmoji;
using DbReport = Valour.Database.Report;
using DbUserPreferences = Valour.Database.UserPreferences;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Sdk.Client;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class BotServiceTests : IAsyncLifetime
{
    private readonly ValourClient _client;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly BotService _botService;
    private readonly UserService _userService;

    private readonly List<User> _createdBots = new();

    public BotServiceTests(LoginTestFixture fixture)
    {
        _client = fixture.Client;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _botService = _scope.ServiceProvider.GetRequiredService<BotService>();
        _userService = _scope.ServiceProvider.GetRequiredService<UserService>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var bot in _createdBots)
        {
            try
            {
                await _userService.HardDelete(bot);
            }
            catch
            {
                // ignore cleanup failures
            }
        }

        _scope.Dispose();
    }

    [Fact]
    public async Task DeleteBotAsync_RemovesBotAndRelatedAccountData()
    {
        var create = await _botService.CreateBotAsync(_client.Me.Id, RandomBotName());
        Assert.True(create.Success, create.Message);

        var bot = create.Data.Bot;
        _createdBots.Add(bot);

        var reportId = Guid.NewGuid().ToString("N");
        var auditLogId = IdManager.Generate();
        var emojiId = IdManager.Generate();

        _db.UserPreferences.Add(new DbUserPreferences
        {
            Id = bot.Id,
            ErrorReportingState = ErrorReportingState.Unset,
            NotificationVolume = NotificationPreferences.DefaultNotificationVolume,
            EnabledNotificationSources = NotificationPreferences.AllNotificationSourcesMask,
            DmPolicy = DmPolicy.Everyone
        });

        _db.Reports.Add(new DbReport
        {
            Id = reportId,
            TimeCreated = DateTime.UtcNow,
            ReportingUserId = _client.Me.Id,
            ReportedUserId = bot.Id,
            ReasonCode = ReportReasonCode.IsSpam,
            LongReason = "regression",
            Reviewed = false,
            Resolution = ReportResolution.None,
            StaffNotes = string.Empty
        });

        _db.ModerationAuditLogs.Add(new DbModerationAuditLog
        {
            Id = auditLogId,
            PlanetId = ISharedPlanet.ValourCentralId,
            ActorUserId = bot.Id,
            TargetUserId = bot.Id,
            Source = ModerationActionSource.Manual,
            ActionType = ModerationActionType.DeleteMessage,
            Details = "regression",
            TimeCreated = DateTime.UtcNow
        });

        _db.PlanetEmojis.Add(new DbPlanetEmoji
        {
            Id = emojiId,
            PlanetId = ISharedPlanet.ValourCentralId,
            CreatorUserId = bot.Id,
            Name = RandomEmojiName(),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var delete = await _botService.DeleteBotAsync(bot.Id, _client.Me.Id);
        Assert.True(delete.Success, delete.Message);
        _createdBots.Remove(bot);

        _db.ChangeTracker.Clear();

        Assert.Null(await _db.Users.FindAsync(bot.Id));
        Assert.Null(await _db.UserPreferences.FindAsync(bot.Id));

        var report = await _db.Reports.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == reportId);
        Assert.NotNull(report);
        Assert.Null(report.ReportedUserId);

        var auditLog = await _db.ModerationAuditLogs.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == auditLogId);
        Assert.NotNull(auditLog);
        Assert.Null(auditLog.ActorUserId);
        Assert.Null(auditLog.TargetUserId);

        Assert.Null(await _db.PlanetEmojis.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == emojiId));
    }

    private static string RandomBotName() =>
        $"bot{Guid.NewGuid():N}".Substring(0, 12);

    private static string RandomEmojiName() =>
        $"e_{Guid.NewGuid():N}".Substring(0, 16);
}
