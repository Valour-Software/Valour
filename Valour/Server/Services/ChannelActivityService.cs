#nullable enable annotations

using StackExchange.Redis;
using Valour.Server.Redis;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Non-ping channel activity notifications: tracks per-channel message bursts
/// in rolling Redis windows, and — when a channel crosses the activity
/// threshold — notifies interested users under a strict frequency budget.
/// Interest (view recency, favorites) sets how often a channel may notify a
/// user; Redis cooldown keys enforce the budget. Candidate resolution runs on
/// the ChannelActivityWorker, never the message hot path.
/// See Docs/ChannelActivityNotifications.md.
/// </summary>
public class ChannelActivityService
{
    private readonly ValourDb _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ChannelActivityWorker _worker;
    private readonly NotificationService _notificationService;
    private readonly ChannelWatchingService _channelWatchingService;
    private readonly PlanetPermissionService _permissionService;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<ChannelActivityService> _logger;

    public ChannelActivityService(
        ValourDb db,
        IConnectionMultiplexer redis,
        ChannelActivityWorker worker,
        NotificationService notificationService,
        ChannelWatchingService channelWatchingService,
        PlanetPermissionService permissionService,
        CoreHubService coreHub,
        ILogger<ChannelActivityService> logger)
    {
        _db = db;
        _redis = redis;
        _worker = worker;
        _notificationService = notificationService;
        _channelWatchingService = channelWatchingService;
        _permissionService = permissionService;
        _coreHub = coreHub;
        _logger = logger;
    }

    private static RedisKey GetMessagesKey(long channelId) => $"chanact:m:{channelId}";
    private static RedisKey GetAuthorsKey(long channelId) => $"chanact:a:{channelId}";
    private static RedisKey GetEvalDebounceKey(long channelId) => $"chanact:eval:{channelId}";
    private static RedisKey GetActiveKey(long channelId) => $"chanact:active:{channelId}";
    private static RedisKey GetCooldownKey(long userId, long channelId) => $"chanact:cd:{userId}:{channelId}";
    private static RedisKey GetGlobalGapKey(long userId) => $"chanact:gap:{userId}";

    /// <summary>
    /// Hot-path hook (called from message posting): bumps the channel's rolling
    /// activity window and, on a debounced threshold crossing, queues a
    /// candidate evaluation on the worker. Redis-only; must stay cheap.
    /// </summary>
    public async Task RecordMessageAsync(long channelId, long planetId, long messageId, long authorUserId)
    {
        var db = _redis.GetDatabase(RedisDbTypes.Cluster);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStartMs = nowMs - (long)ChannelActivityPreferences.ActivityWindow.TotalMilliseconds;
        var messagesKey = GetMessagesKey(channelId);
        var authorsKey = GetAuthorsKey(channelId);
        var keyTtl = ChannelActivityPreferences.ActivityWindow * 2;

        await Task.WhenAll(
            db.SortedSetAddAsync(messagesKey, messageId, nowMs),
            db.SortedSetRemoveRangeByScoreAsync(messagesKey, double.NegativeInfinity, windowStartMs),
            db.KeyExpireAsync(messagesKey, keyTtl),
            db.SortedSetAddAsync(authorsKey, authorUserId, nowMs),
            db.SortedSetRemoveRangeByScoreAsync(authorsKey, double.NegativeInfinity, windowStartMs),
            db.KeyExpireAsync(authorsKey, keyTtl)
        );

        var messageCount = await db.SortedSetLengthAsync(messagesKey);
        if (messageCount < ChannelActivityPreferences.MinWindowMessages)
            return;

        var authorEntries = await db.SortedSetRangeByRankAsync(authorsKey);
        if (authorEntries.Length < ChannelActivityPreferences.MinWindowAuthors)
            return;

        // One evaluation per channel per debounce period
        var acquired = await db.StringSetAsync(
            GetEvalDebounceKey(channelId), 1,
            ChannelActivityPreferences.EvaluationDebounce, When.NotExists);

        var activeKey = GetActiveKey(channelId);

        if (!acquired)
        {
            // Sustained burst: keep the channel marked active so a later
            // evaluation isn't misframed as a conversation start
            await db.KeyExpireAsync(activeKey, ChannelActivityPreferences.ConversationStartGap);
            return;
        }

        // Conversation start = first threshold crossing after a quiet period
        var conversationStart = !await db.KeyExistsAsync(activeKey);
        await db.StringSetAsync(activeKey, 1, ChannelActivityPreferences.ConversationStartGap);

        var authorIds = new List<long>(authorEntries.Length);
        foreach (var entry in authorEntries)
        {
            if (long.TryParse(entry.ToString(), out var id))
                authorIds.Add(id);
        }

        await _worker.QueueEvaluation(new ChannelActivityEvaluation
        {
            ChannelId = channelId,
            PlanetId = planetId,
            TriggerMessageId = messageId,
            WindowMessageCount = (int)messageCount,
            WindowAuthorCount = authorIds.Count,
            WindowAuthorUserIds = authorIds.ToArray(),
            ConversationStart = conversationStart,
        });
    }

    /// <summary>
    /// Worker path: resolves interested users for an active channel, applies
    /// the frequency budget, and sends coalesced activity notifications.
    /// </summary>
    public async Task EvaluateAsync(ChannelActivityEvaluation eval)
    {
        var now = DateTime.UtcNow;

        var planet = (await _db.Planets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eval.PlanetId))?.ToModel();
        if (planet is null)
            return;

        var channelName = await _db.Channels.AsNoTracking()
            .Where(x => x.Id == eval.ChannelId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync();
        if (channelName is null)
            return;

        var planetBaseCooldown = ChannelActivityPreferences.GetBaseCooldown(planet.ActivityNotificationCadence);

        // Per-channel and per-planet hard mutes apply regardless of interest source
        var mutedUserIds = (await _db.UserChannelStates.AsNoTracking()
            .Where(s => s.ChannelId == eval.ChannelId && s.ActivityAlerts == ChannelActivityAlerts.Off)
            .Select(s => s.UserId)
            .ToListAsync()).ToHashSet();

        mutedUserIds.UnionWith(await _db.UserPlanetSettings.AsNoTracking()
            .Where(s => s.PlanetId == eval.PlanetId && s.ActivityAlerts == ChannelActivityAlerts.Off)
            .Select(s => s.UserId)
            .ToListAsync());

        var interestCutoff = now - ChannelActivityPreferences.InterestFloor;
        var viewerStates = await _db.UserChannelStates.AsNoTracking()
            .Where(s => s.ChannelId == eval.ChannelId && s.LastViewedTime >= interestCutoff)
            .OrderByDescending(s => s.LastViewedTime)
            .Take(ChannelActivityPreferences.MaxCandidatesPerEvaluation)
            .Select(s => new { s.UserId, s.LastViewedTime, s.PlanetMemberId })
            .ToArrayAsync();

        var favoriteUserIds = (await _db.ChannelFavorites.AsNoTracking()
            .Where(f => f.ChannelId == eval.ChannelId)
            .Select(f => f.UserId)
            .ToListAsync()).ToHashSet();

        var burstAuthors = eval.WindowAuthorUserIds.ToHashSet();

        var candidates = new Dictionary<long, Candidate>();
        foreach (var state in viewerStates)
        {
            if (mutedUserIds.Contains(state.UserId) || burstAuthors.Contains(state.UserId))
                continue;

            candidates[state.UserId] = new Candidate
            {
                UserId = state.UserId,
                LastViewed = state.LastViewedTime,
                MemberId = state.PlanetMemberId,
                IsFavorite = favoriteUserIds.Contains(state.UserId),
            };
        }

        foreach (var userId in favoriteUserIds)
        {
            if (candidates.Count >= ChannelActivityPreferences.MaxCandidatesPerEvaluation)
                break;
            if (candidates.ContainsKey(userId) || mutedUserIds.Contains(userId) || burstAuthors.Contains(userId))
                continue;

            candidates[userId] = new Candidate
            {
                UserId = userId,
                IsFavorite = true,
            };
        }

        if (candidates.Count == 0)
            return;

        // Resolve planet membership for favorite-only candidates
        var missingMemberIds = candidates.Values
            .Where(c => c.MemberId is null)
            .Select(c => c.UserId)
            .ToArray();

        if (missingMemberIds.Length > 0)
        {
            var members = await _db.PlanetMembers.AsNoTracking()
                .Where(m => m.PlanetId == eval.PlanetId && missingMemberIds.Contains(m.UserId))
                .Select(m => new { m.UserId, m.Id })
                .ToArrayAsync();

            foreach (var member in members)
                candidates[member.UserId].MemberId = member.Id;
        }

        var candidateIds = candidates.Keys.ToArray();
        var preferences = await _db.UserPreferences.AsNoTracking()
            .Where(p => candidateIds.Contains(p.Id))
            .Select(p => new { p.Id, p.EnabledNotificationSources, p.ActivityCooldownSeconds })
            .ToDictionaryAsync(p => p.Id);

        var eligible = new List<Candidate>();
        foreach (var candidate in candidates.Values)
        {
            // Membership required; also gates favorites left over from planets
            // the user has since left
            if (candidate.MemberId is null)
                continue;

            // Anyone who viewed the channel during the current burst window
            // has already seen the activity — even if their watch lease lapsed
            if (candidate.LastViewed is not null &&
                candidate.LastViewed >= now - ChannelActivityPreferences.ActivityWindow)
                continue;

            TimeSpan? baseCooldown = planetBaseCooldown;
            if (preferences.TryGetValue(candidate.UserId, out var prefs))
            {
                if (!NotificationPreferences.IsSourceEnabled(
                        prefs.EnabledNotificationSources, NotificationSource.ChannelActivity))
                    continue;

                if (prefs.ActivityCooldownSeconds is not null)
                {
                    baseCooldown = TimeSpan.FromSeconds(Math.Clamp(
                        prefs.ActivityCooldownSeconds.Value,
                        ChannelActivityPreferences.MinCooldownSeconds,
                        ChannelActivityPreferences.MaxCooldownSeconds));
                }
            }

            if (baseCooldown is null)
                continue;

            var multiplier = ChannelActivityPreferences.GetInterestMultiplier(
                candidate.LastViewed, candidate.IsFavorite, now);
            if (multiplier is null)
                continue;

            if (!await _permissionService.HasChannelAccessAsync(candidate.MemberId.Value, eval.ChannelId))
                continue;

            candidate.EffectiveCooldown = baseCooldown.Value * multiplier.Value;
            eligible.Add(candidate);
        }

        if (eligible.Count == 0)
            return;

        // Never notify users already looking at the channel
        var notViewing = (await _channelWatchingService.FilterUsersNotViewingChannelAsync(
            eval.ChannelId, eligible.Select(c => c.UserId))).ToHashSet();

        var redisDb = _redis.GetDatabase(RedisDbTypes.Cluster);
        var recipients = new List<Candidate>();
        foreach (var candidate in eligible)
        {
            if (!notViewing.Contains(candidate.UserId))
                continue;

            var cooldownKey = GetCooldownKey(candidate.UserId, eval.ChannelId);
            if (await redisDb.KeyExistsAsync(cooldownKey))
                continue;

            // Global gap: at most one activity notification per user per gap
            // period across all channels — competing channels lose the slot
            if (!await redisDb.StringSetAsync(
                    GetGlobalGapKey(candidate.UserId), 1,
                    ChannelActivityPreferences.GlobalUserGap, When.NotExists))
                continue;

            await redisDb.StringSetAsync(cooldownKey, 1, candidate.EffectiveCooldown);
            recipients.Add(candidate);
        }

        if (recipients.Count == 0)
            return;

        var title = eval.ConversationStart
            ? $"#{channelName} is picking up"
            : $"#{channelName} is active";

        var template = new Notification
        {
            Title = title,
            Body = $"{eval.WindowMessageCount} messages from {eval.WindowAuthorCount} people in {planet.Name}",
            ImageUrl = planet.GetIconUrl(IconFormat.Webp128),
            ClickUrl = $"/planetchannels/{eval.PlanetId}/{eval.ChannelId}/{eval.TriggerMessageId}",
            PlanetId = eval.PlanetId,
            ChannelId = eval.ChannelId,
            Source = NotificationSource.ChannelActivity,
            SourceId = eval.TriggerMessageId,
        };

        await _notificationService.SendChannelActivityNotificationsAsync(
            recipients.Select(c => c.UserId).ToArray(), template);

        _logger.LogInformation(
            "Channel activity ({Kind}) in channel {ChannelId}: {CandidateCount} candidates, {RecipientCount} notified",
            eval.ConversationStart ? "start" : "ongoing",
            eval.ChannelId,
            candidates.Count,
            recipients.Count);
    }

    /// <summary>
    /// Called when a user views a channel: marks their coalesced activity
    /// notification read. The cooldown is deliberately NOT reset — it is a
    /// hard budget. Read-state updates fire constantly while a client has a
    /// channel open, and resetting the cooldown from them turns "once per
    /// cooldown period" into "once per evaluation" during a burst.
    /// </summary>
    public async Task HandleChannelViewedAsync(long userId, long channelId)
    {
        try
        {
            await _notificationService.MarkChannelActivityNotificationsReadAsync(userId, channelId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to clear channel activity state for user {UserId} in channel {ChannelId}",
                userId, channelId);
        }
    }

    /// <summary>
    /// Upserts the user's per-channel activity alert override without
    /// disturbing their read state.
    /// </summary>
    public async Task<TaskResult<UserChannelState>> SetActivityAlertsAsync(
        long channelId,
        long userId,
        long? planetId,
        long? memberId,
        ChannelActivityAlerts setting)
    {
        // Rows created purely to hold the override get an epoch view time so
        // they never count as having viewed the channel
        var epoch = DateTime.UnixEpoch;

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO user_channel_states (channel_id, user_id, last_viewed_time, planet_id, member_id, activity_alerts)
            VALUES ({channelId}, {userId}, {epoch}, {planetId}, {memberId}, {(int)setting})
            ON CONFLICT (user_id, channel_id) DO UPDATE
            SET activity_alerts = EXCLUDED.activity_alerts
        ");

        var state = await _db.UserChannelStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ChannelId == channelId);

        if (state is null)
            return TaskResult<UserChannelState>.FromFailure("Failed to load state after update");

        var model = state.ToModel();
        _coreHub.NotifyUserChannelStateUpdate(userId, model);

        return TaskResult<UserChannelState>.FromData(model);
    }

    /// <summary>
    /// Returns the user's planet-wide activity alert override
    /// </summary>
    public async Task<ChannelActivityAlerts> GetPlanetActivityAlertsAsync(long planetId, long userId)
    {
        var setting = await _db.UserPlanetSettings.AsNoTracking()
            .Where(x => x.UserId == userId && x.PlanetId == planetId)
            .Select(x => (ChannelActivityAlerts?)x.ActivityAlerts)
            .FirstOrDefaultAsync();

        return setting ?? ChannelActivityAlerts.Auto;
    }

    /// <summary>
    /// Upserts the user's planet-wide activity alert override
    /// </summary>
    public async Task<TaskResult> SetPlanetActivityAlertsAsync(long planetId, long userId, ChannelActivityAlerts setting)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO user_planet_settings (user_id, planet_id, activity_alerts)
            VALUES ({userId}, {planetId}, {(int)setting})
            ON CONFLICT (user_id, planet_id) DO UPDATE
            SET activity_alerts = EXCLUDED.activity_alerts
        ");

        return TaskResult.SuccessResult;
    }

    private class Candidate
    {
        public long UserId;
        public DateTime? LastViewed;
        public long? MemberId;
        public bool IsFavorite;
        public TimeSpan EffectiveCooldown;
    }
}
