using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Valour.Sdk.E2ee;
using Valour.Server.Hubs;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Server.Services;

/// <summary>
/// Lets automod word and command triggers work in end-to-end encrypted
/// channels. A moderator's client hashes each trigger word with the channel's
/// index key and uploads the result. Senders upload the same kind of hashes
/// for their messages, so the server can match them before storing a message
/// without learning any words.
/// </summary>
public class E2eeAutomodService
{
    private const int MaxTermsPerAlternative = 16;

    private static readonly TimeSpan WorkNoticeInterval = TimeSpan.FromMinutes(2);

    /// <summary>Entries each static cache keeps before it drops some (see <see cref="E2eeCacheLimit"/>).</summary>
    private const int MaxCacheEntries = 50_000;

    /// <summary>
    /// Term hashes the term cache holds before it starts over. One channel's
    /// triggers can hold up to 500 alternatives of 16 terms each, so the cache
    /// is bounded by terms rather than by channels. This is about 16 MB.
    /// </summary>
    private const long MaxCachedTerms = 4_000_000;

    private static readonly ConcurrentDictionary<(long ChannelId, int IndexGeneration), Dictionary<Guid, List<int[]>>>
        TermCache = new();
    private static long _cachedTerms;
    private static readonly ConcurrentDictionary<long, DateTime> LastWorkNotice = new();
    private static readonly ConcurrentDictionary<(long ChannelId, int Generation), int> IndexGenerationCache = new();

    private readonly ValourDb _db;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly PlanetPermissionService _permissionService;
    private readonly IHubContext<CoreHub> _hub;
    private readonly E2eeAccessLogService _accessLogs;

    public E2eeAutomodService(ValourDb db, HostedPlanetService hostedPlanetService,
        PlanetPermissionService permissionService, IHubContext<CoreHub> hub, E2eeAccessLogService accessLogs)
    {
        _db = db;
        _hostedPlanetService = hostedPlanetService;
        _permissionService = permissionService;
        _hub = hub;
        _accessLogs = accessLogs;
    }

    public static bool UsesTerms(AutomodTriggerType type) =>
        type is AutomodTriggerType.Blacklist or AutomodTriggerType.Command;

    /// <summary>
    /// The index generation whose key produced a message's terms.
    /// </summary>
    private async Task<int> GetIndexGenerationAsync(long channelId, int generation)
    {
        if (IndexGenerationCache.TryGetValue((channelId, generation), out var cached))
            return cached;

        var index = await _db.E2eeChannelKeyGenerations.AsNoTracking()
            .Where(x => x.ChannelId == channelId && x.Generation == generation)
            .Select(x => x.IndexGeneration)
            .FirstOrDefaultAsync();
        if (index > 0)
        {
            E2eeCacheLimit.Trim(IndexGenerationCache, MaxCacheEntries);
            IndexGenerationCache[(channelId, generation)] = index;
        }
        return index;
    }

    private async Task<Dictionary<Guid, List<int[]>>> GetTermSetsAsync(long channelId, int indexGeneration)
    {
        if (TermCache.TryGetValue((channelId, indexGeneration), out var cached))
            return cached;

        var rows = await _db.E2eeAutomodTerms.AsNoTracking()
            .Where(x => x.ChannelId == channelId && x.IndexGeneration == indexGeneration)
            .ToListAsync();

        var sets = rows.GroupBy(x => x.TriggerId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Terms).ToList());

        // Counted as at least one term, so channels without triggers count too.
        var size = Math.Max(1, rows.Sum(x => (long)x.Terms.Length));
        if (Interlocked.Add(ref _cachedTerms, size) > MaxCachedTerms)
            ClearTermCache();
        TermCache[(channelId, indexGeneration)] = sets;
        return sets;
    }

    private static void ClearTermCache()
    {
        TermCache.Clear();
        Interlocked.Exchange(ref _cachedTerms, 0);
    }

    /// <summary>
    /// Returns true if an encrypted message's terms satisfy the trigger. When
    /// no moderator has hashed the trigger for this channel yet, it cannot
    /// match, and moderators' clients are asked to do it. The caller passes
    /// only triggers for which <see cref="UsesTerms"/> is true.
    /// </summary>
    public async Task<bool> MatchesAsync(AutomodTrigger trigger, Message message)
    {
        if (message.IndexedTerms is null)
            return false;

        var indexGeneration = await GetIndexGenerationAsync(message.ChannelId, message.KeyGeneration);
        if (indexGeneration == 0)
            return false;

        var sets = await GetTermSetsAsync(message.ChannelId, indexGeneration);
        if (!sets.TryGetValue(trigger.Id, out var alternatives))
        {
            // Moderators' apps never hash a trigger an admin has not approved
            // in an invite-only planet, so asking them again would not help.
            if (!IsWorkNoticeThrottled(trigger.PlanetId) &&
                (await GetApprovalFilterAsync(trigger.PlanetId))(trigger.Id, trigger.Type, trigger.TriggerWords))
                await RequestWorkAsync(trigger.PlanetId);
            return false;
        }

        var terms = message.IndexedTerms.ToHashSet();
        return alternatives.Any(alt => alt.Length > 0 && alt.All(terms.Contains));
    }

    /// <summary>
    /// Discards hashes for a trigger whose words changed or that was deleted.
    /// </summary>
    public async Task InvalidateTriggerAsync(Guid triggerId, long planetId)
    {
        await _db.E2eeAutomodTerms.Where(x => x.TriggerId == triggerId).ExecuteDeleteAsync();
        ClearTermCache();
        LastWorkNotice.TryRemove(planetId, out _);
        await RequestWorkAsync(planetId);
    }

    private static bool IsWorkNoticeThrottled(long planetId) =>
        LastWorkNotice.TryGetValue(planetId, out var last) && DateTime.UtcNow - last < WorkNoticeInterval;

    /// <summary>
    /// Which triggers moderators' apps will hash. In an invite-only planet
    /// they only hash triggers whose current type and text an admin approved
    /// in the membership log (see <see cref="AccessLogState.IsAutomodTriggerApproved"/>),
    /// so the server lists and asks about only those. A planet whose log does
    /// not verify gets no work.
    /// </summary>
    private async Task<Func<Guid, AutomodTriggerType, string, bool>> GetApprovalFilterAsync(long planetId)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        if (hosted.Planet.EncryptionMode != PlanetEncryptionMode.InviteOnly)
            return (_, _, _) => true;

        var log = await _accessLogs.GetStateAsync(AccessLogScope.Planet, planetId);
        if (log is null)
            return (_, _, _) => false;
        return (id, type, words) => log.IsAutomodTriggerApproved(id, (int)type, words);
    }

    /// <summary>
    /// Asks moderators' clients in the planet to compute missing hashes.
    /// </summary>
    public async Task RequestWorkAsync(long planetId)
    {
        var now = DateTime.UtcNow;
        if (LastWorkNotice.TryGetValue(planetId, out var last) && now - last < WorkNoticeInterval)
            return;
        E2eeCacheLimit.Trim(LastWorkNotice, MaxCacheEntries);
        LastWorkNotice[planetId] = now;

        await _hub.Clients.Group($"p-{planetId}").SendAsync(E2eeRealtimeEvent.HubMethod, new E2eeRealtimeEvent
        {
            Type = E2eeRealtimeEventTypes.AutomodTermsNeeded,
            PlanetId = planetId
        });
    }

    /// <summary>
    /// Lists the hashes a moderator's client should compute: every word or
    /// command trigger in every encrypted channel they can read that lacks
    /// hashes for the channel's current index key.
    /// </summary>
    public async Task<TaskResult<List<AutomodTermsWorkDto>>> GetWorkAsync(long planetId, PlanetMember member)
    {
        if (!await _permissionService.HasPlanetPermissionAsync(member, PlanetPermissions.Manage))
            return TaskResult<List<AutomodTermsWorkDto>>.FromFailure("You cannot manage automod in this planet.");

        var approved = await GetApprovalFilterAsync(planetId);
        var triggers = (await _db.AutomodTriggers.AsNoTracking()
            .Where(x => x.PlanetId == planetId &&
                        (x.Type == AutomodTriggerType.Blacklist || x.Type == AutomodTriggerType.Command))
            .ToListAsync())
            .Where(t => approved(t.Id, t.Type, t.TriggerWords))
            .ToList();
        if (triggers.Count == 0)
            return TaskResult<List<AutomodTermsWorkDto>>.FromData([]);

        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        var work = new List<AutomodTermsWorkDto>();

        foreach (var channel in hosted.Channels.List.Where(c => c.EncryptionGeneration > 0))
        {
            if (!await _permissionService.CanUserViewChannelAsync(hosted, member.UserId, channel.Id))
                continue;

            var indexGeneration = await GetIndexGenerationAsync(channel.Id, channel.EncryptionGeneration);
            if (indexGeneration == 0)
                continue;

            var covered = await _db.E2eeAutomodTerms.AsNoTracking()
                .Where(x => x.ChannelId == channel.Id && x.IndexGeneration == indexGeneration)
                .Select(x => x.TriggerId)
                .Distinct()
                .ToListAsync();
            var coveredSet = covered.ToHashSet();

            var missing = triggers.Where(t => !coveredSet.Contains(t.Id)).ToList();
            if (missing.Count == 0)
                continue;

            work.Add(new AutomodTermsWorkDto
            {
                ChannelId = channel.Id,
                IndexGeneration = indexGeneration,
                Triggers = missing.Select(t => new AutomodTriggerWorkDto
                {
                    TriggerId = t.Id,
                    Type = (int)t.Type,
                    TriggerWords = t.TriggerWords
                }).ToList()
            });
        }

        return TaskResult<List<AutomodTermsWorkDto>>.FromData(work);
    }

    public async Task<TaskResult> SaveAsync(long planetId, PlanetMember member, List<AutomodTermsUploadDto> uploads)
    {
        if (!await _permissionService.HasPlanetPermissionAsync(member, PlanetPermissions.Manage))
            return TaskResult.FromFailure("You cannot manage automod in this planet.");
        if (uploads is null || uploads.Count == 0)
            return TaskResult.SuccessResult;
        if (uploads.Count > 200)
            return TaskResult.FromFailure("Too many uploads.");

        var migrationLock = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationLock.Success)
            return migrationLock;
        if (uploads.DistinctBy(u => (u.TriggerId, u.ChannelId, u.IndexGeneration)).Count() != uploads.Count)
            return TaskResult.FromFailure("Each trigger can be uploaded once per channel and search key.");

        // Every upload is checked before anything changes, so a request with
        // one bad upload leaves the stored terms as they were.
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        var rows = new List<Valour.Database.E2eeAutomodTerm>();
        foreach (var upload in uploads)
        {
            var channel = hosted.GetChannel(upload.ChannelId);
            if (channel is null || channel.EncryptionGeneration == 0)
                return TaskResult.FromFailure("Channel is not an encrypted channel in this planet.");
            if (!await _permissionService.CanUserViewChannelAsync(hosted, member.UserId, channel.Id))
                return TaskResult.FromFailure("You cannot view that channel.");

            var exists = await _db.E2eeChannelKeyGenerations.AnyAsync(x =>
                x.ChannelId == channel.Id && x.IndexGeneration == upload.IndexGeneration);
            if (!exists)
                return TaskResult.FromFailure("Unknown index key.");

            var trigger = await _db.AutomodTriggers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == upload.TriggerId && x.PlanetId == planetId);
            if (trigger is null || !UsesTerms(trigger.Type))
                return TaskResult.FromFailure("Trigger not found.");

            var alternatives = upload.Alternatives ?? [];
            if (alternatives.Count > E2eeLimits.MaxAutomodAlternatives ||
                alternatives.Any(a => a is null || a.Length == 0 || a.Length > MaxTermsPerAlternative))
                return TaskResult.FromFailure("Invalid trigger terms.");

            // A trigger with no usable words still gets an empty-set row so it
            // is not reported as missing again; empty sets never match.
            if (alternatives.Count == 0)
                alternatives = [[]];

            rows.AddRange(alternatives.Select(alternative => new Valour.Database.E2eeAutomodTerm
            {
                TriggerId = upload.TriggerId,
                PlanetId = planetId,
                ChannelId = channel.Id,
                IndexGeneration = upload.IndexGeneration,
                Terms = SearchTerms.Normalize(alternative)
            }));
        }

        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            // Terms for an older search key are dropped along with the ones
            // being replaced. Members send with the channel's newest key, so
            // only its terms are matched, and a channel's terms would
            // otherwise grow with every search key it has had.
            foreach (var upload in uploads)
            {
                await _db.E2eeAutomodTerms
                    .Where(x => x.TriggerId == upload.TriggerId && x.ChannelId == upload.ChannelId &&
                                x.IndexGeneration <= upload.IndexGeneration)
                    .ExecuteDeleteAsync();
            }

            _db.E2eeAutomodTerms.AddRange(rows);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        ClearTermCache();
        return TaskResult.SuccessResult;
    }
}
