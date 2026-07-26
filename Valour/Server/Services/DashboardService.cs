using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;
using Valour.Config.Configs;
using Valour.Server.Hubs;
using Valour.Server.Redis;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Server.Services;

/// <summary>
/// Builds the staff dashboard's live counters, point-in-time snapshot and
/// historical analytics. Reads only aggregate platform data — never location,
/// IP or device information.
/// </summary>
public class DashboardService
{
    private readonly ValourDb _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DashboardService> _logger;

    /// <summary>
    /// The platform's Online threshold
    /// </summary>
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan ActiveNowWindow = TimeSpan.FromMinutes(15);

    public DashboardService(
        ValourDb db,
        IConnectionMultiplexer redis,
        ILogger<DashboardService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    ////////////////
    // Live stats //
    ////////////////

    public async Task<DashboardLiveStats> BuildLiveStatsAsync()
    {
        var nodes = await GetClusterNodeStatesAsync();
        var voice = await GetVoiceChannelParticipantsAsync();
        return await BuildLiveStatsAsync(nodes, voice);
    }

    private async Task<DashboardLiveStats> BuildLiveStatsAsync(
        List<ClusterNodeState> nodes,
        List<(long ChannelId, int Participants)> voice)
    {
        var now = DateTime.UtcNow;
        var onlineCutoff = now - OnlineWindow;

        var onlineUsers = await _db.Users.AsNoTracking()
            .CountAsync(x => !x.Bot && x.TimeLastActive > onlineCutoff);

        // Prefer the cluster-wide sums each node publishes; fall back to the
        // local counters when no nodestats entries exist (single-node dev)
        var aliveStats = nodes
            .Where(x => x.Alive && x.Stats is not null)
            .Select(x => x.Stats)
            .ToList();

        int connections, primaryConnections;
        if (aliveStats.Count > 0)
        {
            connections = aliveStats.Sum(x => x.Connections);
            primaryConnections = aliveStats.Sum(x => x.PrimaryConnections);
        }
        else
        {
            connections = SignalRConnectionService.TotalConnections;
            primaryConnections = SignalRConnectionService.TotalPrimaryConnections;
        }

        // `planet:*` cannot match the `planet-claim:{id}` lock keys: glob
        // patterns are literal outside wildcards, and `-` is not `:`
        var hostedPlanets = CountRedisKeys("planet:*");

        var statCutoff = now.AddMinutes(-5);
        var messagesPerMinute = await _db.Stats.AsNoTracking()
            .Where(x => x.TimeCreated > statCutoff)
            .OrderByDescending(x => x.TimeCreated)
            .Select(x => (int?)x.MessagesSent)
            .FirstOrDefaultAsync() ?? 0;

        return new DashboardLiveStats
        {
            TimeUtc = now,
            OnlineUsers = onlineUsers,
            Connections = connections,
            PrimaryConnections = primaryConnections,
            VoiceChannels = voice.Count,
            VoiceParticipants = voice.Sum(x => x.Participants),
            HostedPlanets = hostedPlanets,
            MessagesPerMinute = messagesPerMinute,
        };
    }

    //////////////
    // Snapshot //
    //////////////

    public async Task<DashboardSnapshot> BuildSnapshotAsync()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var todayDay = DateOnly.FromDateTime(now);
        var activeCutoff = now - ActiveNowWindow;
        var dayCutoff = now.AddHours(-24);

        var nodes = await GetClusterNodeStatesAsync();
        var voice = await GetVoiceChannelParticipantsAsync();
        var live = await BuildLiveStatsAsync(nodes, voice);

        var totalUsers = await _db.Users.AsNoTracking().LongCountAsync(x => !x.Bot);
        var totalPlanets = await _db.Planets.AsNoTracking().LongCountAsync();

        // The minute-level stat row already carries a full message count;
        // only fall back to the expensive table count when it is stale
        var latestStat = await _db.Stats.AsNoTracking()
            .OrderByDescending(x => x.TimeCreated)
            .Select(x => new { x.TimeCreated, x.MessageDayCount })
            .FirstOrDefaultAsync();

        var totalMessages = latestStat is not null && latestStat.TimeCreated > now.AddMinutes(-10)
            ? latestStat.MessageDayCount
            : await _db.Messages.AsNoTracking().LongCountAsync();

        var dailyActiveUsers = await _db.UserActivityDays.AsNoTracking()
            .CountAsync(x => x.Day == todayDay);

        var mauCutoff = todayDay.AddDays(-30);
        var monthlyActiveUsers = await _db.UserActivityDays.AsNoTracking()
            .Where(x => x.Day > mauCutoff)
            .Select(x => x.UserId)
            .Distinct()
            .CountAsync();

        var signupsToday = await _db.Users.AsNoTracking()
            .CountAsync(x => x.TimeJoined >= today);

        var activePlanets = await _db.PlanetMembers.AsNoTracking()
            .Where(x => x.TimeLastConnected > activeCutoff)
            .Select(x => x.PlanetId)
            .Distinct()
            .CountAsync();

        return new DashboardSnapshot
        {
            TimeUtc = now,
            Live = live,
            TotalUsers = totalUsers,
            TotalPlanets = totalPlanets,
            TotalMessages = totalMessages,
            DailyActiveUsers = dailyActiveUsers,
            MonthlyActiveUsers = monthlyActiveUsers,
            SignupsToday = signupsToday,
            ActivePlanets = activePlanets,
            ActiveVoiceChannels = await BuildActiveVoiceChannelsAsync(voice),
            TopPlanets = await BuildTopPlanetsAsync(activeCutoff, dayCutoff),
            GlobePlanets = await BuildGlobePlanetsAsync(),
            ClusterNodes = BuildClusterNodes(nodes),
            Federation = await BuildFederationInfoAsync(now),
            Revenue = await BuildRevenueAsync(now, today),
        };
    }

    private async Task<List<DashboardVoiceChannel>> BuildActiveVoiceChannelsAsync(
        List<(long ChannelId, int Participants)> voice)
    {
        var top = voice
            .OrderByDescending(x => x.Participants)
            .Take(20)
            .ToList();

        if (top.Count == 0)
            return new List<DashboardVoiceChannel>();

        var channelIds = top.Select(x => x.ChannelId).ToArray();
        var channels = await _db.Channels.AsNoTracking()
            .Where(x => channelIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.PlanetId })
            .ToListAsync();

        var planetIds = channels
            .Where(x => x.PlanetId is not null)
            .Select(x => x.PlanetId.Value)
            .Distinct()
            .ToArray();

        var planetNames = await _db.Planets.AsNoTracking()
            .Where(x => planetIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        var result = new List<DashboardVoiceChannel>(top.Count);
        foreach (var (channelId, participants) in top)
        {
            var channel = channels.FirstOrDefault(x => x.Id == channelId);
            if (channel is null)
                continue;

            result.Add(new DashboardVoiceChannel
            {
                ChannelId = channelId,
                ChannelName = channel.Name,
                PlanetId = channel.PlanetId,
                PlanetName = channel.PlanetId is not null
                    ? planetNames.GetValueOrDefault(channel.PlanetId.Value)
                    : null,
                ParticipantCount = participants,
            });
        }

        return result;
    }

    private async Task<List<DashboardTopPlanet>> BuildTopPlanetsAsync(DateTime activeCutoff, DateTime dayCutoff)
    {
        var topActivity = await _db.PlanetMembers.AsNoTracking()
            .Where(x => x.TimeLastConnected > dayCutoff)
            .GroupBy(x => x.PlanetId)
            .Select(g => new { PlanetId = g.Key, ActiveToday = g.Count() })
            .OrderByDescending(x => x.ActiveToday)
            .Take(10)
            .ToListAsync();

        if (topActivity.Count == 0)
            return new List<DashboardTopPlanet>();

        var topIds = topActivity.Select(x => x.PlanetId).ToArray();

        var planets = await _db.Planets.AsNoTracking()
            .Where(x => topIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.HasCustomIcon,
                x.HasAnimatedIcon,
                x.Version,
                MemberCount = x.Members.Count(m => !m.IsDeleted),
                ActiveNow = x.Members.Count(m => !m.IsDeleted && m.TimeLastConnected > activeCutoff),
            })
            .ToListAsync();

        var messagesToday = await _db.Messages.AsNoTracking()
            .Where(x => x.PlanetId != null && topIds.Contains(x.PlanetId.Value) && x.TimeSent > dayCutoff)
            .GroupBy(x => x.PlanetId.Value)
            .Select(g => new { PlanetId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanetId, x => x.Count);

        var result = new List<DashboardTopPlanet>(topActivity.Count);
        foreach (var activity in topActivity)
        {
            var planet = planets.FirstOrDefault(x => x.Id == activity.PlanetId);
            if (planet is null)
                continue;

            result.Add(new DashboardTopPlanet
            {
                PlanetId = planet.Id,
                Name = planet.Name,
                IconUrl = ResolveIconUrl(planet.Id, planet.Name, planet.HasCustomIcon, planet.HasAnimatedIcon, planet.Version),
                MemberCount = planet.MemberCount,
                ActiveNow = planet.ActiveNow,
                ActiveToday = activity.ActiveToday,
                MessagesToday = messagesToday.GetValueOrDefault(planet.Id),
            });
        }

        return result;
    }

    /// <summary>
    /// Resolves a planet's icon URL the same way PlanetListInfo consumers do,
    /// but returns null for the generated default icon so the payload stays
    /// small and the client can render its own placeholder.
    /// </summary>
    private static string ResolveIconUrl(long planetId, string name, bool hasCustomIcon, bool hasAnimatedIcon, int version)
    {
        if (!hasCustomIcon)
            return null;

        return ISharedPlanet.GetIconUrl(new PlanetListInfo
        {
            PlanetId = planetId,
            Name = name,
            HasCustomIcon = hasCustomIcon,
            HasAnimatedIcon = hasAnimatedIcon,
            Version = version,
        }, IconFormat.Webp256);
    }

    private Task<List<DashboardGlobePlanet>> BuildGlobePlanetsAsync()
    {
        var activeCutoff = DateTime.UtcNow - ActiveNowWindow;

        return _db.Planets.AsNoTracking()
            .Select(x => new DashboardGlobePlanet
            {
                PlanetId = x.Id,
                Name = x.Name,
                MemberCount = x.Members.Count(m => !m.IsDeleted),
                OnlineCount = x.Members.Count(m => !m.IsDeleted && m.TimeLastConnected > activeCutoff),
            })
            .OrderByDescending(x => x.MemberCount)
            .Take(300)
            .ToListAsync();
    }

    private static List<DashboardNodeInfo> BuildClusterNodes(List<ClusterNodeState> nodes)
    {
        var selfName = NodeConfig.Instance.Name;

        return nodes
            .Select(x => new DashboardNodeInfo
            {
                Name = x.Name,
                IsSelf = x.Name == selfName,
                Alive = x.Alive,
                LastSeenUtc = x.LastSeenUtc,
                Connections = x.Stats?.Connections ?? 0,
                PrimaryConnections = x.Stats?.PrimaryConnections ?? 0,
                Groups = x.Stats?.Groups ?? 0,
                HostedPlanets = x.Stats?.HostedPlanets ?? 0,
                CpuPercent = x.Stats?.CpuPercent ?? -1,
                MemoryMb = x.Stats?.MemoryMb ?? -1,
                UptimeSeconds = x.Stats?.UptimeSeconds ?? 0,
                Version = x.Stats?.Version,
            })
            .OrderBy(x => x.Name)
            .ToList();
    }

    private async Task<DashboardFederationInfo> BuildFederationInfoAsync(DateTime now)
    {
        var statusCounts = await _db.FederatedNodes.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var activeCutoff = now.AddDays(-7);
        var activeLast7Days = await _db.FederatedNodes.AsNoTracking()
            .CountAsync(x => x.Status == Valour.Database.FederatedNodeStatus.Active &&
                             x.LastSeenAt > activeCutoff);

        return new DashboardFederationInfo
        {
            TotalNodes = statusCounts.Sum(x => x.Count),
            VerifiedNodes = statusCounts
                .FirstOrDefault(x => x.Status == Valour.Database.FederatedNodeStatus.Active)?.Count ?? 0,
            PendingNodes = statusCounts
                .FirstOrDefault(x => x.Status == Valour.Database.FederatedNodeStatus.PendingVerification)?.Count ?? 0,
            SuspendedNodes = statusCounts
                .FirstOrDefault(x => x.Status == Valour.Database.FederatedNodeStatus.Suspended)?.Count ?? 0,
            ActiveLast7Days = activeLast7Days,
        };
    }

    /////////////
    // Revenue //
    /////////////

    private async Task<DashboardRevenue> BuildRevenueAsync(DateTime now, DateTime today)
    {
        var subGroups = await _db.UserSubscriptions.AsNoTracking()
            .Where(x => x.Active)
            .GroupBy(x => new { x.Type, IsStripe = x.StripeSubscriptionId != null })
            .Select(g => new { g.Key.Type, g.Key.IsStripe, Count = g.Count() })
            .ToListAsync();

        var tiers = new List<DashboardRevenueTier>();
        foreach (var (type, typeInfo) in UserSubscriptionTypes.TypeMap)
        {
            var stripeCount = subGroups.FirstOrDefault(x => x.Type == type && x.IsStripe)?.Count ?? 0;
            var vcCount = subGroups.FirstOrDefault(x => x.Type == type && !x.IsStripe)?.Count ?? 0;

            tiers.Add(new DashboardRevenueTier
            {
                Type = type,
                StripeCount = stripeCount,
                VcCount = vcCount,
                MrrCents = stripeCount * typeInfo.StripePriceCents,
            });
        }

        var revenueEntries = await GetStripeRevenueEntriesAsync(now.AddDays(-30), now);

        return new DashboardRevenue
        {
            MrrCents = tiers.Sum(x => x.MrrCents),
            ActiveStripeSubscriptions = subGroups.Where(x => x.IsStripe).Sum(x => x.Count),
            ActiveVcSubscriptions = subGroups.Where(x => !x.IsStripe).Sum(x => x.Count),
            TodayCents = revenueEntries.Where(x => x.TimeStamp >= today).Sum(x => x.Cents),
            Last30DaysCents = revenueEntries.Sum(x => x.Cents),
            Tiers = tiers,
        };
    }

    private const string OneTimeFingerprintPrefix = "stripe_checkout:";

    private sealed record StripeRevenueEntry(DateTime TimeStamp, bool OneTime, long Cents);

    /// <summary>
    /// Loads and classifies real-money Stripe revenue from the Victor payout
    /// transactions. `stripe_checkout:` fingerprints are one-time credit-pack
    /// purchases; every other stripe prefix (`stripe_reward_initial:`,
    /// `stripe_reward_invoice:`) is a monthly subscription charge. Stripe
    /// amounts are never persisted locally, so USD cents are derived from the
    /// deposited VC amount via the product and tier constants.
    /// </summary>
    private async Task<List<StripeRevenueEntry>> GetStripeRevenueEntriesAsync(DateTime startUtc, DateTime endUtc)
    {
        var rows = await _db.Transactions.AsNoTracking()
            .Where(x => x.UserFromId == ISharedUser.VictorUserId &&
                        x.Fingerprint != null &&
                        x.Fingerprint.StartsWith("stripe") &&
                        x.TimeStamp >= startUtc &&
                        x.TimeStamp < endUtc)
            .Select(x => new { x.TimeStamp, x.Fingerprint, x.Amount })
            .ToListAsync();

        var entries = new List<StripeRevenueEntry>(rows.Count);
        foreach (var row in rows)
        {
            var oneTime = row.Fingerprint.StartsWith(OneTimeFingerprintPrefix, StringComparison.Ordinal);
            var cents = oneTime
                ? CreditPackCents(row.Amount)
                : SubscriptionChargeCents(row.Amount);

            entries.Add(new StripeRevenueEntry(row.TimeStamp, oneTime, cents));
        }

        return entries;
    }

    /// <summary>
    /// VC500 $5.00, VC1000 $9.50, VC2000 $18.00 — unknown packs count as zero
    /// rather than guessing
    /// </summary>
    private static long CreditPackCents(decimal vcAmount) => vcAmount switch
    {
        500m => 500,
        1000m => 950,
        2000m => 1800,
        _ => 0,
    };

    /// <summary>
    /// Tier VC rewards map back to the Stripe monthly price:
    /// 50 → $4.99, 100 → $9.99, 150 → $14.99
    /// </summary>
    private static long SubscriptionChargeCents(decimal vcReward) => vcReward switch
    {
        50m => 499,
        100m => 999,
        150m => 1499,
        _ => 0,
    };

    ///////////////
    // Analytics //
    ///////////////

    private sealed class DayValueRow
    {
        public DateTime Day { get; set; }
        public long Value { get; set; }
    }

    public async Task<DashboardAnalytics> BuildAnalyticsAsync(int days)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var startDay = today.AddDays(-(days - 1));
        var startDate = DateOnly.FromDateTime(startDay);
        var endDate = DateOnly.FromDateTime(today);

        // Daily active users
        var dauRows = await _db.UserActivityDays.AsNoTracking()
            .Where(x => x.Day >= startDate)
            .GroupBy(x => x.Day)
            .Select(g => new { Day = g.Key, Count = g.LongCount() })
            .ToListAsync();

        var dau = FillDailySeries(
            dauRows.ToDictionary(x => x.Day.ToDateTime(TimeOnly.MinValue), x => x.Count),
            startDay, days);

        // Rolling 30-day distinct active users, one point per day. The
        // correlated subquery walks the (day, user_id) primary key, so this
        // stays cheap even for a full-year range.
        var mauRows = await _db.Database.SqlQuery<DayValueRow>($"""
            SELECT gs.day AS "Day",
                   (SELECT COUNT(DISTINCT u.user_id)
                      FROM user_activity_days u
                     WHERE u.day > (gs.day - INTERVAL '30 days')::date
                       AND u.day <= gs.day::date) AS "Value"
              FROM generate_series({startDate}, {endDate}, INTERVAL '1 day') AS gs(day)
            """).ToListAsync();

        var mau = FillDailySeries(
            mauRows.ToDictionary(x => x.Day, x => x.Value),
            startDay, days);

        // All remaining daily groupings bucket timestamptz columns explicitly
        // at UTC so the series never shift with the database session timezone
        var startTs = DateTime.SpecifyKind(startDay, DateTimeKind.Utc);

        var signupRows = await _db.Database.SqlQuery<DayValueRow>($"""
            SELECT ((u.time_joined AT TIME ZONE 'UTC')::date)::timestamp AS "Day",
                   COUNT(*)::bigint AS "Value"
              FROM users u
             WHERE u.time_joined >= {startTs}
             GROUP BY 1
            """).ToListAsync();

        var signups = FillDailySeries(
            signupRows.ToDictionary(x => x.Day, x => x.Value),
            startDay, days);

        var messageDayRows = await _db.Database.SqlQuery<DayValueRow>($"""
            SELECT ((s.time_created AT TIME ZONE 'UTC')::date)::timestamp AS "Day",
                   COALESCE(SUM(s.messages_sent), 0)::bigint AS "Value"
              FROM stat_objects s
             WHERE s.time_created >= {startTs}
             GROUP BY 1
            """).ToListAsync();

        var messagesPerDay = FillDailySeries(
            messageDayRows.ToDictionary(x => x.Day, x => x.Value),
            startDay, days);

        // Messages per hour over the last 48 hours
        const int hourWindow = 48;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var startHour = currentHour.AddHours(-(hourWindow - 1));

        var messageHourRows = await _db.Database.SqlQuery<DayValueRow>($"""
            SELECT date_trunc('hour', s.time_created AT TIME ZONE 'UTC') AS "Day",
                   COALESCE(SUM(s.messages_sent), 0)::bigint AS "Value"
              FROM stat_objects s
             WHERE s.time_created >= {startHour}
             GROUP BY 1
            """).ToListAsync();

        var messagesPerHour = FillHourlySeries(
            messageHourRows.ToDictionary(x => x.Day, x => x.Value),
            startHour, hourWindow);

        // Revenue per day, split by source
        var revenueEntries = await GetStripeRevenueEntriesAsync(startTs, now);
        var revenueByDay = revenueEntries
            .GroupBy(x => x.TimeStamp.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var revenuePerDay = new List<DashboardRevenuePoint>(days);
        for (var i = 0; i < days; i++)
        {
            var day = DateTime.SpecifyKind(startDay.AddDays(i), DateTimeKind.Utc);
            revenueByDay.TryGetValue(day, out var entries);

            revenuePerDay.Add(new DashboardRevenuePoint
            {
                TimeUtc = day,
                OneTimeCents = entries?.Where(x => x.OneTime).Sum(x => x.Cents) ?? 0,
                SubscriptionCents = entries?.Where(x => !x.OneTime).Sum(x => x.Cents) ?? 0,
            });
        }

        return new DashboardAnalytics
        {
            Days = days,
            DailyActiveUsers = dau,
            MonthlyActiveUsers = mau,
            Signups = signups,
            MessagesPerDay = messagesPerDay,
            MessagesPerHour = messagesPerHour,
            RevenuePerDay = revenuePerDay,
        };
    }

    /// <summary>
    /// Zero-fills a daily series so charts never skip missing days. DateTime
    /// dictionary keys compare by ticks, so mixed Kind values still match.
    /// </summary>
    private static List<DashboardTimePoint> FillDailySeries(
        Dictionary<DateTime, long> values, DateTime startDay, int days)
    {
        var series = new List<DashboardTimePoint>(days);
        for (var i = 0; i < days; i++)
        {
            var day = DateTime.SpecifyKind(startDay.AddDays(i), DateTimeKind.Utc);
            values.TryGetValue(day, out var value);
            series.Add(new DashboardTimePoint { TimeUtc = day, Value = value });
        }

        return series;
    }

    private static List<DashboardTimePoint> FillHourlySeries(
        Dictionary<DateTime, long> values, DateTime startHour, int hours)
    {
        var series = new List<DashboardTimePoint>(hours);
        for (var i = 0; i < hours; i++)
        {
            var hour = DateTime.SpecifyKind(startHour.AddHours(i), DateTimeKind.Utc);
            values.TryGetValue(hour, out var value);
            series.Add(new DashboardTimePoint { TimeUtc = hour, Value = value });
        }

        return series;
    }

    ///////////////////
    // Redis reading //
    ///////////////////

    private sealed record ClusterNodeState(string Name, DateTime LastSeenUtc, bool Alive, NodeRuntimeStats Stats);

    /// <summary>
    /// Assembles the official cluster view from the `alive:{node}` liveness
    /// keys, merging in each node's published `nodestats:{node}` payload when
    /// present. Liveness keys have no TTL, so dead nodes appear with
    /// Alive = false rather than vanishing.
    /// </summary>
    private async Task<List<ClusterNodeState>> GetClusterNodeStatesAsync()
    {
        var result = new List<ClusterNodeState>();
        var server = _redis.GetServers().FirstOrDefault();
        if (server is null)
            return result;

        var db = _redis.GetDatabase(RedisDbTypes.Cluster);
        var now = DateTime.UtcNow;

        foreach (var key in server.Keys(RedisDbTypes.Cluster, "alive:*"))
        {
            var name = key.ToString().Substring("alive:".Length);

            var aliveValue = await db.StringGetAsync(key);
            if (aliveValue.IsNull)
                continue;

            if (!DateTime.TryParse(aliveValue!, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out var lastSeen))
                continue;

            var alive = (now - lastSeen).TotalSeconds < 60;

            NodeRuntimeStats stats = null;
            var statsValue = await db.StringGetAsync($"nodestats:{name}");
            if (!statsValue.IsNull)
            {
                try
                {
                    stats = JsonSerializer.Deserialize<NodeRuntimeStats>((string)statsValue!);
                }
                catch (JsonException e)
                {
                    _logger.LogWarning(e, "Ignoring malformed nodestats entry for node {Node}", name);
                }
            }

            result.Add(new ClusterNodeState(name, lastSeen, alive, stats));
        }

        return result;
    }

    private async Task<List<(long ChannelId, int Participants)>> GetVoiceChannelParticipantsAsync()
    {
        var result = new List<(long, int)>();
        var server = _redis.GetServers().FirstOrDefault();
        if (server is null)
            return result;

        var db = _redis.GetDatabase(RedisDbTypes.Cluster);

        foreach (var key in server.Keys(RedisDbTypes.Cluster, "voice:channel:*"))
        {
            var channelIdStr = key.ToString().Replace("voice:channel:", "");
            if (!long.TryParse(channelIdStr, out var channelId))
                continue;

            var participants = (int)await db.SetLengthAsync(key);
            if (participants > 0)
                result.Add((channelId, participants));
        }

        return result;
    }

    private int CountRedisKeys(string pattern)
    {
        var server = _redis.GetServers().FirstOrDefault();
        if (server is null)
            return 0;

        return server.Keys(RedisDbTypes.Cluster, pattern).Count();
    }
}
