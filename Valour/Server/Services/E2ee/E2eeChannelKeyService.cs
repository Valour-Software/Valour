using System.Security.Cryptography;
using System.Collections.Concurrent;
using Npgsql;
using NpgsqlTypes;
using Valour.Config.Configs;
using Valour.Sdk.E2ee;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Server.Services;

/// <summary>
/// Stores channel key generations and the boxes members seal for each other.
///
/// Distribution is pull-based: a member without the newest key asks for it,
/// and any online member who holds it seals it to them. A key rotation
/// therefore costs the rotating member one box, no matter how large the
/// channel is.
///
/// Every chat channel is encrypted. When the server must seal a message into
/// a channel that has no key yet, it creates the first key itself (see
/// <see cref="EnsureSealingGenerationAsync"/>). Members replace that key before
/// they send anything.
/// </summary>
public class E2eeChannelKeyService
{
    /// <summary>
    /// Generations a member loads with a channel's keys beyond the ones they
    /// hold. Older records are fetched when a member reads older messages.
    /// </summary>
    public const int RecentGenerations = 32;

    /// <summary>Key holders asked directly when someone requests a planet channel's key.</summary>
    private const int MaxNotifiedHolders = 25;

    /// <summary>Search keys a member searches with, newest first.</summary>
    public const int MaxSearchIndexGenerations = E2eeLimits.MaxSearchIndexGenerations;

    private const string AutomodKeyMessage =
        "This planet filters words with automod, so a member who has this channel's key needs to be online to share it before you can send.";

    /// <summary>
    /// What a cached rotation check depends on. A change to any of them
    /// means the check runs again.
    /// </summary>
    private readonly record struct RotationInputs(int Generation, long PermissionGeneration, long MembershipVersion,
        int AccessLogHead, long UserKeyChanges, long ViewersHash);

    private sealed record RotationCheck(RotationInputs Inputs, bool Required, DateTime? RecheckAt,
        DateTime ExpiresAt);

    /// <summary>
    /// How long a rotation check is reused at most. Other server processes
    /// sharing the database do not see this process's key changes, so a
    /// result that looks current is still recomputed after this long.
    /// </summary>
    private static readonly TimeSpan RotationCheckLifetime = TimeSpan.FromMinutes(1);

    /// <summary>How far ahead of the server's clock a key record may be dated.</summary>
    private static readonly TimeSpan MaxRecordClockSkew = TimeSpan.FromMinutes(10);

    /// <summary>A key request newer than this is not announced again.</summary>
    private static readonly TimeSpan KeyRequestRepeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>Channels each static cache keeps before it drops some (see <see cref="E2eeCacheLimit"/>).</summary>
    private const int MaxCachedChannels = 50_000;

    // Rotation checks compute every viewer and holder, and run for every
    // message sent, so the result is kept until one of its inputs changes.
    private static readonly ConcurrentDictionary<long, RotationCheck> RotationCache = new();

    // Generations never change once published, so each channel's links are
    // cached and extended with newer generations only.
    private static readonly ConcurrentDictionary<long, Dictionary<int, ChannelKeyChain.Link>> LinkCache = new();

    // Generations known to protect at least one message, by channel, so the
    // flag is only written once per generation.
    private static readonly ConcurrentDictionary<long, ConcurrentDictionary<int, byte>> UsedGenerations = new();

    private readonly ValourDb _db;
    private readonly E2eeIdentityService _identity;
    private readonly E2eeAccessLogService _accessLogs;
    private readonly E2eeRealtimeService _realtime;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly PlanetPermissionService _permissionService;
    private readonly CoreHubService _coreHub;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly E2eeServerKeyService _serverKeys;
    private readonly E2eeAutomodService _automod;
    private readonly ILogger<E2eeChannelKeyService> _logger;

    public E2eeChannelKeyService(
        ValourDb db,
        E2eeIdentityService identity,
        E2eeAccessLogService accessLogs,
        E2eeRealtimeService realtime,
        HostedPlanetService hostedPlanetService,
        PlanetPermissionService permissionService,
        CoreHubService coreHub,
        NodeLifecycleService nodeLifecycleService,
        E2eeServerKeyService serverKeys,
        E2eeAutomodService automod,
        ILogger<E2eeChannelKeyService> logger)
    {
        _db = db;
        _identity = identity;
        _accessLogs = accessLogs;
        _realtime = realtime;
        _hostedPlanetService = hostedPlanetService;
        _permissionService = permissionService;
        _coreHub = coreHub;
        _nodeLifecycleService = nodeLifecycleService;
        _serverKeys = serverKeys;
        _automod = automod;
        _logger = logger;
    }

    public static bool SupportsEncryption(ISharedChannel channel) =>
        channel.ChannelType is ChannelTypeEnum.PlanetChat or ChannelTypeEnum.DirectChat or ChannelTypeEnum.GroupChat;

    private async Task<(ChannelKeyPolicy Policy, bool SharesHistory)> GetPolicyAsync(Channel channel)
    {
        if (channel.PlanetId is null)
            return (channel.ChannelType == ChannelTypeEnum.GroupChat ? ChannelKeyPolicy.Group : ChannelKeyPolicy.Direct, true);

        var hosted = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
        var policy = hosted.Planet.EncryptionMode == PlanetEncryptionMode.InviteOnly
            ? ChannelKeyPolicy.PlanetInviteOnly
            : ChannelKeyPolicy.PlanetOpen;
        return (policy, hosted.Planet.EncryptionSharesHistory);
    }

    public async Task<ChannelKeyStateDto> GetStateAsync(Channel channel, long userId)
    {
        var (policy, sharesHistory) = await GetPolicyAsync(channel);
        var latest = await GetLatestGenerationAsync(channel.Id);
        if (latest > 0)
            await DeliverHeldKeyAsync(channel, userId, latest, sharesHistory);

        var boxes = await _db.E2eeChannelKeyBoxes.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.UserId == userId)
            .OrderBy(x => x.Generation)
            .Select(x => new ChannelKeyBoxDto
            {
                ChannelId = x.ChannelId,
                Generation = x.Generation,
                UserId = x.UserId,
                UserKeyGeneration = x.UserKeyGeneration,
                Box = x.Box
            })
            .ToListAsync();

        // The caller receives the recent generations and the ones it holds,
        // with their search key generations. Older records are fetched when
        // they are needed to read older messages.
        var wanted = boxes.Select(b => b.Generation)
            .Concat(Enumerable.Range(Math.Max(1, latest - RecentGenerations + 1), Math.Min(latest, RecentGenerations)))
            .ToHashSet();
        var indexGenerations = await _db.E2eeChannelKeyGenerations.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && wanted.Contains(x.Generation))
            .Select(x => x.IndexGeneration)
            .ToListAsync();
        wanted.UnionWith(indexGenerations);

        var generations = await _db.E2eeChannelKeyGenerations.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && wanted.Contains(x.Generation))
            .OrderBy(x => x.Generation)
            .Select(x => new ChannelKeyGenerationEntry
            {
                ChannelId = x.ChannelId,
                Generation = x.Generation,
                Body = x.Body,
                Signature = x.Signature
            })
            .ToListAsync();

        return new ChannelKeyStateDto
        {
            Policy = policy,
            SharesHistory = sharesHistory,
            LatestGeneration = latest,
            RotationRequired = latest > 0 && await IsRotationRequiredAsync(channel, latest),
            Generations = generations,
            MyBoxes = boxes,
            IndexGenerations = await _db.E2eeChannelKeyGenerations.AsNoTracking()
                .Where(x => x.ChannelId == channel.Id)
                .Select(x => x.IndexGeneration)
                .Distinct()
                .OrderByDescending(g => g)
                .Take(MaxSearchIndexGenerations)
                .ToListAsync()
        };
    }

    /// <summary>Signed generation records in a range, for reading older messages.</summary>
    public Task<List<ChannelKeyGenerationEntry>> GetGenerationsAsync(Channel channel, int from, int to) =>
        _db.E2eeChannelKeyGenerations.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Generation >= from && x.Generation <= to)
            .OrderBy(x => x.Generation)
            .Take(E2eeLimits.MaxBoxesPerRequest)
            .Select(x => new ChannelKeyGenerationEntry
            {
                ChannelId = x.ChannelId,
                Generation = x.Generation,
                Body = x.Body,
                Signature = x.Signature
            })
            .ToListAsync();

    /// <summary>
    /// Records that a generation protects a message. A generation that does
    /// not unlock earlier keys and protects nothing can be given to several
    /// new members of a planet that hides history without revealing anything.
    /// </summary>
    public async Task MarkGenerationUsedAsync(long channelId, int generation)
    {
        if (UsedGenerations.TryGetValue(channelId, out var known) && known.ContainsKey(generation))
            return;

        await _db.E2eeChannelKeyGenerations
            .Where(x => x.ChannelId == channelId && x.Generation == generation && !x.HasMessages)
            .ExecuteUpdateAsync(x => x.SetProperty(g => g.HasMessages, true));

        // Cached only after the write succeeds, so a failed write is retried.
        E2eeCacheLimit.Trim(UsedGenerations, MaxCachedChannels);
        UsedGenerations.GetOrAdd(channelId, _ => new ConcurrentDictionary<int, byte>())[generation] = 0;
    }

    /// <summary>Forgets cached key data for a channel whose records were deleted or replaced.</summary>
    public static void ForgetChannel(long channelId)
    {
        LinkCache.TryRemove(channelId, out _);
        RotationCache.TryRemove(channelId, out _);
        UsedGenerations.TryRemove(channelId, out _);
    }

    public Task<int> GetLatestGenerationAsync(long channelId) =>
        _db.Channels.Where(x => x.Id == channelId).Select(x => x.EncryptionGeneration).FirstAsync();

    public async Task<Valour.Database.E2eeChannelKeyGeneration> GetGenerationAsync(long channelId, int generation) =>
        await _db.E2eeChannelKeyGenerations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChannelId == channelId && x.Generation == generation);

    // Who may hold keys

    private async Task<List<long>> GetViewerUserIdsAsync(Channel channel)
    {
        if (channel.PlanetId is null)
        {
            return await _db.ChannelMembers.AsNoTracking()
                .Where(x => x.ChannelId == channel.Id)
                .Select(x => x.UserId)
                .ToListAsync();
        }

        var hosted = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
        return await _permissionService.GetAllChannelViewerUserIdsAsync(hosted, channel.Id);
    }

    public async Task<bool> CanViewAsync(Channel channel, long userId)
    {
        if (channel.PlanetId is null)
            return await _db.ChannelMembers.AnyAsync(x => x.ChannelId == channel.Id && x.UserId == userId);

        var hosted = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
        return await _permissionService.CanUserViewChannelAsync(hosted, userId, channel.Id);
    }

    private async Task<bool> CanPostAsync(Channel channel, long userId)
    {
        if (!await CanViewAsync(channel, userId))
            return false;
        if (channel.PlanetId is null)
            return true;

        var hosted = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
        return hosted.TryGetMemberByUser(userId, out var member) &&
               await _permissionService.HasChannelPermissionAsync(member, channel, ChatChannelPermissions.PostMessages);
    }

    private async Task<bool> IsModeratorAsync(long planetId, long userId)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hosted.TryGetMemberByUser(userId, out var member) &&
               await _permissionService.HasPlanetPermissionAsync(member, PlanetPermissions.Manage);
    }

    /// <summary>
    /// True when members must replace the newest key before sending: the
    /// server created it, or someone who holds it can no longer view the
    /// channel. Large open planets replace keys after departures on a
    /// schedule instead, because members leave them constantly and anyone can
    /// join them again.
    ///
    /// Every message sent runs this check, so the result is cached until the
    /// key, the planet's permissions or membership, the chat's members, the
    /// access log, or any user's key changes (see <see cref="RotationInputs"/>),
    /// and for at most <see cref="RotationCheckLifetime"/>.
    /// </summary>
    public async Task<bool> IsRotationRequiredAsync(Channel channel, int latestGeneration)
    {
        // Inputs are read before computing, so a change during the check
        // leaves a cached result that no longer matches.
        var userKeyChanges = E2eeIdentityService.UserKeyChanges;
        var (policy, _) = await GetPolicyAsync(channel);

        HostedPlanet hosted = null;
        List<long> directViewers = null;
        long permissionGeneration = 0, membershipVersion = 0, viewersHash = 0;
        if (channel.PlanetId is not null)
        {
            hosted = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
            permissionGeneration = hosted.PermissionCache.Generation;
            membershipVersion = hosted.MembershipVersion;
        }
        else
        {
            // Direct and group chat membership has no version number, so the
            // member list itself, one small query, is part of the cache key.
            directViewers = await GetViewerUserIdsAsync(channel);
            viewersHash = HashUserIds(directViewers);
        }

        var accessLogHead = -1;
        if (GetAccessLogScope(channel, policy) is { } logScope)
        {
            accessLogHead = await _db.E2eeAccessLogEntries.AsNoTracking()
                .Where(x => x.Scope == (int)logScope.Scope && x.ScopeId == logScope.ScopeId)
                .MaxAsync(x => (int?)x.Seq) ?? -1;
        }

        var inputs = new RotationInputs(latestGeneration, permissionGeneration, membershipVersion, accessLogHead,
            userKeyChanges, viewersHash);
        var now = DateTime.UtcNow;
        if (RotationCache.TryGetValue(channel.Id, out var cached) && cached.Inputs == inputs &&
            cached.ExpiresAt > now && (cached.RecheckAt is null || cached.RecheckAt > now))
            return cached.Required;

        var (required, recheckAt) = await ComputeRotationRequiredAsync(channel, latestGeneration, policy, hosted,
            directViewers);

        E2eeCacheLimit.Trim(RotationCache, MaxCachedChannels);
        RotationCache[channel.Id] = new RotationCheck(inputs, required, recheckAt, now + RotationCheckLifetime);
        return required;
    }

    private async Task<(bool Required, DateTime? RecheckAt)> ComputeRotationRequiredAsync(Channel channel,
        int latestGeneration, ChannelKeyPolicy policy, HostedPlanet hosted, List<long> directViewers)
    {
        var latest = await GetGenerationAsync(channel.Id, latestGeneration);
        if (latest is null)
            return (false, null);
        if (latest.CreatorUserId == ChannelKeyGenerationRecord.ServerCreatorId)
            return (true, null);

        var boxes = await _db.E2eeChannelKeyBoxes.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Generation == latestGeneration)
            .Select(x => new { x.UserId, x.UserKeyGeneration })
            .ToListAsync();
        var holders = boxes.Select(x => x.UserId).ToList();

        var viewers = (directViewers ?? await GetViewerUserIdsAsync(channel)).ToHashSet();
        var required = holders.Any(h => !viewers.Contains(h));

        // A box sealed to a user key that was replaced, because a device was
        // removed or the keys were reset, may still be opened by the removed
        // device, so later messages need a key it never had.
        if (!required)
        {
            var states = await _identity.GetStatesAsync(holders);
            required = boxes.Any(b => states.TryGetValue(b.UserId, out var state) && state.HasIdentity &&
                                      b.UserKeyGeneration < state.UserKey.Generation);
        }

        // In governed channels, a holder removed from the access log must also
        // lose access to later messages.
        if (!required && await GetGoverningAccessLogAsync(channel, policy) is { } log)
            required = holders.Any(h => !log.IsMemberAnyEpoch(h));

        DateTime? recheckAt = null;
        var config = E2eeConfig.Current;
        if (required && policy == ChannelKeyPolicy.PlanetOpen && hosted is not null &&
            hosted.MemberCount >= config.LargePlanetMembers)
        {
            var due = latest.CreatedAt + TimeSpan.FromDays(Math.Max(1, config.LargePlanetRotationDays));
            if (DateTime.UtcNow < due)
            {
                required = false;
                recheckAt = due;
            }
        }

        return (required, recheckAt);
    }

    /// <summary>The access log that governs a channel, or null for open planets and direct messages.</summary>
    private static (AccessLogScope Scope, long ScopeId)? GetAccessLogScope(Channel channel, ChannelKeyPolicy policy) =>
        policy switch
        {
            ChannelKeyPolicy.Group => (AccessLogScope.GroupChannel, channel.Id),
            ChannelKeyPolicy.PlanetInviteOnly => (AccessLogScope.Planet, channel.PlanetId!.Value),
            _ => null
        };

    /// <summary>
    /// The verified access log that governs a channel, or null for open
    /// planets and direct messages, or when the governing log does not exist
    /// or does not verify.
    /// </summary>
    private async Task<AccessLogState> GetGoverningAccessLogAsync(Channel channel, ChannelKeyPolicy policy) =>
        GetAccessLogScope(channel, policy) is { } scope
            ? await _accessLogs.GetStateAsync(scope.Scope, scope.ScopeId)
            : null;

    /// <summary>An order-independent fingerprint of a list of users, for cache keys.</summary>
    private static long HashUserIds(List<long> userIds)
    {
        userIds.Sort();
        var hash = new HashCode();
        foreach (var id in userIds)
            hash.Add(id);
        return ((long)hash.ToHashCode() << 32) | (uint)userIds.Count;
    }

    public static void ForgetRotationCheck(long channelId) => RotationCache.TryRemove(channelId, out _);

    // Generations

    public async Task<TaskResult<ChannelKeyStateDto>> CreateGenerationAsync(Channel channel, long userId,
        CreateChannelKeyGenerationRequest request)
    {
        if (!SupportsEncryption(channel))
            return TaskResult<ChannelKeyStateDto>.FromFailure("This channel cannot be encrypted.");
        if (request?.Entry?.Body is null || request.Entry.Signature is null)
            return TaskResult<ChannelKeyStateDto>.FromFailure("Include the signed key generation.");
        if (request.Entry.Body.Length > 4096)
            return TaskResult<ChannelKeyStateDto>.FromFailure("Key generation is too large.");

        if (channel.PlanetId is not null)
        {
            var hosted = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
            if (hosted.Planet.LockedForMigration)
                return TaskResult<ChannelKeyStateDto>.FromFailure(MigrationLock.Message);
        }

        // Everyone sends with the newest key, so only members who can post
        // may publish one. A read-only viewer could otherwise publish a key
        // sealed only to themselves and stop everyone else from sending.
        if (!await CanPostAsync(channel, userId))
            return TaskResult<ChannelKeyStateDto>.FromFailure("You cannot send messages in this channel.");

        var creatorState = await _identity.GetStateAsync(userId);
        if (creatorState is null)
            return TaskResult<ChannelKeyStateDto>.FromFailure("Set up encryption first.");

        ChannelKeyGenerationRecord record;
        try
        {
            record = ChannelKeyGenerationRecord.Verify(request.Entry, creatorState);
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return TaskResult<ChannelKeyStateDto>.FromFailure(e.Message);
        }

        if (record.IsServerCreated)
            return TaskResult<ChannelKeyStateDto>.FromFailure("Only the server creates server keys.");
        if (creatorState.GetActiveSigner(record.CreatorDeviceId) is null)
            return TaskResult<ChannelKeyStateDto>.FromFailure("The signing device is not active.");
        if (record.ChannelId != channel.Id || record.PlanetId != (channel.PlanetId ?? 0) || record.CreatorUserId != userId)
            return TaskResult<ChannelKeyStateDto>.FromFailure("The key generation does not match this channel.");

        var latest = await GetLatestGenerationAsync(channel.Id);
        if (record.Generation != latest + 1)
            return TaskResult<ChannelKeyStateDto>.FromFailure("Another member published a newer key. Refresh and try again.");

        // Devices order keys and messages by the times signed into records,
        // so a record dated far ahead would make every later key look older
        // than it and could keep members from trusting them.
        var latestAllowedTime = DateTimeOffset.UtcNow.Add(MaxRecordClockSkew).ToUnixTimeMilliseconds();
        if (record.TimestampMs > latestAllowedTime)
            return TaskResult<ChannelKeyStateDto>.FromFailure("This device's clock is ahead. Correct it and try again.");

        // The rules a key was made under are signed into it, and other
        // members' devices follow them, so they must match the channel.
        // Members of governed channels carry the history setting forward from
        // the key its owner signed, so only open planets must match it here.
        var (policy, sharesHistory) = await GetPolicyAsync(channel);
        if (record.Terms.Policy != policy ||
            (policy == ChannelKeyPolicy.PlanetOpen && record.Terms.SharesHistory != sharesHistory))
            return TaskResult<ChannelKeyStateDto>.FromFailure(
                "This channel's encryption settings changed. Refresh and try again.");

        var creatorAdmitted = await RequireAdmittedAsync(channel, userId, creatorState);
        if (!creatorAdmitted.Success)
            return TaskResult<ChannelKeyStateDto>.FromFailure(creatorAdmitted.Message);

        // Devices share keys only with a membership log that reaches the
        // entry a key names, so a key naming an entry that does not exist
        // would stop every device from sharing the channel's keys.
        if (IsGoverned(policy) &&
            record.Terms.AccessLogSeq > ((await GetGoverningAccessLogAsync(channel, policy))?.HeadSeq ?? -1))
            return TaskResult<ChannelKeyStateDto>.FromFailure(
                "The key names a membership log entry that does not exist. Refresh and try again.");

        if (latest > 0)
        {
            var previous = await GetGenerationAsync(channel.Id, latest);
            if (record.IndexGeneration != previous.IndexGeneration && record.IndexGeneration != record.Generation)
                return TaskResult<ChannelKeyStateDto>.FromFailure("Invalid index generation.");
            // Record times never go backwards. A previous record dated further
            // ahead than any device may date one, or one that cannot be read,
            // is not held against the next.
            var previousTime = TryDecodeRecord(previous.Body)?.TimestampMs ?? 0;
            if (record.TimestampMs < previousTime && previousTime <= latestAllowedTime)
                return TaskResult<ChannelKeyStateDto>.FromFailure(
                    "This key is dated before the channel's current key. Check this device's clock and try again.");

            // The server knew its own key's index key, so members start a new one.
            var previousIndex = await GetGenerationAsync(channel.Id, previous.IndexGeneration);
            if (previousIndex?.CreatorUserId == ChannelKeyGenerationRecord.ServerCreatorId &&
                record.IndexGeneration != record.Generation)
                return TaskResult<ChannelKeyStateDto>.FromFailure("Replace the server's search key with a new one.");

            if (!record.UnlocksPrevious)
            {
                var breakResult = await ValidateHistoryBreakAsync(channel, userId, record, latest,
                    sharesHistory, IsGoverned(policy));
                if (!breakResult.Success)
                    return TaskResult<ChannelKeyStateDto>.FromFailure(breakResult.Message);
            }
            else if (record.IndexGeneration == record.Generation &&
                     previousIndex?.CreatorUserId != ChannelKeyGenerationRecord.ServerCreatorId &&
                     channel.PlanetId is { } planetId)
            {
                // A new search key has no automod term hashes until a
                // moderator's app computes them, so only moderators replace one.
                if (!await IsModeratorAsync(planetId, userId))
                    return TaskResult<ChannelKeyStateDto>.FromFailure("Only moderators can replace this channel's search key.");
            }
        }
        else if (record.IndexGeneration != 1)
        {
            return TaskResult<ChannelKeyStateDto>.FromFailure("The first key generation must be its own index generation.");
        }

        // A key that claims to unlock the current one must come from someone
        // who holds the current one. The server cannot check the claim, and
        // members' devices rely on it when deciding which boxes to keep.
        if (latest > 0 && record.UnlocksPrevious &&
            !await _db.E2eeChannelKeyBoxes.AnyAsync(x =>
                x.ChannelId == channel.Id && x.UserId == userId && x.Generation == latest))
            return TaskResult<ChannelKeyStateDto>.FromFailure("Get this channel's current key before replacing it.");

        var boxes = request.Boxes ?? [];
        if (boxes.All(b => b.UserId != userId))
            return TaskResult<ChannelKeyStateDto>.FromFailure("Include a key box for yourself.");
        if (boxes.Count > E2eeLimits.MaxBoxesPerRequest)
            return TaskResult<ChannelKeyStateDto>.FromFailure("Too many key boxes. Share the rest separately.");
        if (boxes.Any(b => b.Generation != record.Generation))
            return TaskResult<ChannelKeyStateDto>.FromFailure("Key box is for a different key.");

        var boxResult = await ValidateBoxesAsync(channel, boxes);
        if (!boxResult.Success)
            return TaskResult<ChannelKeyStateDto>.FromFailure(boxResult.Message);

        // Boxes for members whose keys changed since the creator loaded them
        // are left out, and the creator shares with them again. The creator's
        // own box must be current, or the new key would have no holder.
        if (boxResult.Data.Stale.Contains(userId))
            return TaskResult<ChannelKeyStateDto>.FromFailure("Your keys changed on another device. Refresh and try again.");

        var now = DateTime.UtcNow;
        _db.E2eeChannelKeyGenerations.Add(new Valour.Database.E2eeChannelKeyGeneration
        {
            ChannelId = channel.Id,
            Generation = record.Generation,
            Body = request.Entry.Body,
            Signature = request.Entry.Signature,
            CreatorUserId = userId,
            SealPublicKey = record.SealPublicKey,
            IndexGeneration = record.IndexGeneration,
            CreatedAt = now
        });
        AddBoxes(boxResult.Data.Valid, userId, now);

        if (!await SaveAndAdvanceGenerationAsync(channel.Id, latest, record.Generation))
            return TaskResult<ChannelKeyStateDto>.FromFailure("Another member published a newer key. Refresh and try again.");

        ForgetRotationCheck(channel.Id);
        await ForgetHeldKeyIfSharedAsync(channel);
        await RemoveFulfilledRequestsAsync(channel, boxResult.Data.Valid.Select(b => b.UserId));
        await PublishGenerationAsync(channel, record.Generation);

        // Moderators' apps compute automod hashes for a new search key right away.
        if (record.IndexGeneration == record.Generation && channel.PlanetId is { } automodPlanetId)
            await _automod.RequestWorkAsync(automodPlanetId);

        var state = await GetStateAsync(channel, userId);
        state.StaleRecipients = boxResult.Data.Stale;
        return TaskResult<ChannelKeyStateDto>.FromData(state);
    }

    /// <summary>
    /// A generation that does not unlock the previous one is allowed in two
    /// cases: a planet that hides history starts a key for a new member, or a
    /// member who cannot get the current key starts one so they can send. It
    /// always has its own search key, because members who only hold it cannot
    /// reach the earlier one.
    /// </summary>
    private async Task<TaskResult> ValidateHistoryBreakAsync(Channel channel, long userId,
        ChannelKeyGenerationRecord record, int latest, bool sharesHistory, bool governed)
    {
        if (record.IndexGeneration != record.Generation)
            return TaskResult.FromFailure("A key that does not unlock earlier keys needs its own search key.");

        // In governed channels the setting signed into members' keys decides
        // whether newcomers read history, and members' devices check it. The
        // planet's current setting may differ until the owner's next key.
        var allowed = record.Reason switch
        {
            ChannelKeyRotationReason.NewMemberWithoutHistory => governed || !sharesHistory,
            ChannelKeyRotationReason.KeyUnavailable => true,
            _ => false
        };
        if (!allowed)
            return TaskResult.FromFailure("A new key must unlock the previous one.");

        var links = await GetLinksAsync(channel.Id);
        var held = (await GetOpenableGenerationsAsync(channel.Id, [userId])).GetValueOrDefault(userId) ?? [];
        var holdsLatest = ChannelKeyChain.Reachable(links, held).Contains(latest);
        if (holdsLatest)
            return record.Reason == ChannelKeyRotationReason.KeyUnavailable
                ? TaskResult.FromFailure("You already hold this channel's key.")
                : TaskResult.SuccessResult;

        // A member without the current key starts a new search key, which has
        // no automod term hashes until a moderator's app computes them. Word
        // filters would not apply to their messages in the meantime, so only
        // moderators may do this where the planet has word or command
        // triggers; their apps compute the hashes right away. This also lets a
        // moderator recover a channel whose newest key nobody else holds.
        if (channel.PlanetId is { } planetId && await _db.AutomodTriggers.AnyAsync(t =>
                t.PlanetId == planetId &&
                (t.Type == AutomodTriggerType.Blacklist || t.Type == AutomodTriggerType.Command)) &&
            !await IsModeratorAsync(planetId, userId))
            return TaskResult.FromFailure(AutomodKeyMessage);

        return TaskResult.SuccessResult;
    }

    // Chains

    private async Task<Dictionary<int, ChannelKeyChain.Link>> GetLinksAsync(long channelId)
    {
        LinkCache.TryGetValue(channelId, out var cached);
        var after = cached is { Count: > 0 } ? cached.Keys.Max() : 0;
        var bodies = await _db.E2eeChannelKeyGenerations.AsNoTracking()
            .Where(x => x.ChannelId == channelId && x.Generation > after)
            .Select(x => new { x.Generation, x.Body })
            .ToListAsync();
        if (bodies.Count == 0 && cached is not null)
            return cached;

        var links = cached is null ? new Dictionary<int, ChannelKeyChain.Link>() : new(cached);
        foreach (var item in bodies)
        {
            // A record that cannot be read is treated as a break in the chain,
            // so members are asked for that generation directly and no box is
            // pruned on the assumption that it unlocks an earlier one.
            var record = TryDecodeRecord(item.Body);
            links[item.Generation] = record is null
                ? new ChannelKeyChain.Link(false, ChannelKeyRotationReason.Initial)
                : new ChannelKeyChain.Link(record.UnlocksPrevious, record.Reason);
        }

        E2eeCacheLimit.Trim(LinkCache, MaxCachedChannels);
        LinkCache[channelId] = links;
        return links;
    }

    /// <summary>
    /// Reads a stored key record, or returns null when it is not in a format
    /// this server reads. Records are checked when they are published, so this
    /// only happens with data written by an earlier, incompatible build.
    /// </summary>
    private static ChannelKeyGenerationRecord TryDecodeRecord(byte[] body)
    {
        try
        {
            return ChannelKeyGenerationRecord.Decode(body);
        }
        catch (E2eeFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes a member's boxes for generations from <paramref name="floor"/>
    /// up to <paramref name="through"/> that their box for
    /// <paramref name="through"/> unlocks. The server cannot check that a
    /// generation really unlocks the previous one, so only the member's own
    /// device asks for this, for the part of the chain it opened itself.
    /// </summary>
    public async Task PruneOwnBoxesAsync(long channelId, long userId, int through, int floor)
    {
        var holdsThrough = await _db.E2eeChannelKeyBoxes.AnyAsync(x =>
            x.ChannelId == channelId && x.UserId == userId && x.Generation == through);
        if (!holdsThrough)
            return;

        var redundant = ChannelKeyChain.Reachable(await GetLinksAsync(channelId), [through]);
        redundant.Remove(through);
        if (redundant.Count == 0)
            return;

        var generations = redundant.Where(g => g >= floor).ToList();
        if (generations.Count == 0)
            return;

        await _db.E2eeChannelKeyBoxes
            .Where(x => x.ChannelId == channelId && x.UserId == userId && generations.Contains(x.Generation))
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// Removes a box a member could not open, so another member can share a
    /// working one.
    /// </summary>
    public async Task DeleteOwnBoxAsync(long channelId, long userId, int generation) =>
        await _db.E2eeChannelKeyBoxes
            .Where(x => x.ChannelId == channelId && x.UserId == userId && x.Generation == generation)
            .ExecuteDeleteAsync();

    /// <summary>
    /// For each user, the generations they are missing and may receive: the
    /// newest one and the head of each earlier chain they cannot reach. In a
    /// channel that hides history from new members, only the newest one, so
    /// a request for earlier chains nobody may share does not stay open.
    /// </summary>
    private async Task<Dictionary<long, List<int>>> GetMissingGenerationsAsync(Channel channel, int latest,
        IReadOnlyCollection<long> userIds)
    {
        var result = new Dictionary<long, List<int>>();
        if (userIds.Count == 0 || latest == 0)
            return result;

        var (_, sharesHistory) = await GetPolicyAsync(channel);
        var links = await GetLinksAsync(channel.Id);
        var heads = sharesHistory ? ChannelKeyChain.Heads(links, latest) : [latest];
        var held = await GetOpenableGenerationsAsync(channel.Id, userIds);

        foreach (var userId in userIds.Distinct())
        {
            var reachable = ChannelKeyChain.Reachable(links, held.GetValueOrDefault(userId) ?? []);
            result[userId] = heads.Where(h => !reachable.Contains(h)).ToList();
        }

        return result;
    }

    /// <summary>
    /// The generations each user holds a box for that they can still open.
    /// A box sealed to a user key from before the user's last key reset can
    /// never be opened again, so it does not count as holding that key.
    /// </summary>
    private async Task<Dictionary<long, List<int>>> GetOpenableGenerationsAsync(long channelId,
        IReadOnlyCollection<long> userIds)
    {
        if (userIds.Count == 0)
            return new Dictionary<long, List<int>>();

        var boxes = await _db.E2eeChannelKeyBoxes.AsNoTracking()
            .Where(x => x.ChannelId == channelId && userIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.Generation, x.UserKeyGeneration })
            .ToListAsync();
        if (boxes.Count == 0)
            return new Dictionary<long, List<int>>();

        var states = await _identity.GetStatesAsync(boxes.Select(b => b.UserId));
        return boxes
            .Where(b => states.TryGetValue(b.UserId, out var state) && state.HasIdentity &&
                        b.UserKeyGeneration >= state.EpochFirstGeneration)
            .GroupBy(b => b.UserId)
            .ToDictionary(g => g.Key, g => g.Select(b => b.Generation).ToList());
    }

    /// <summary>
    /// Viewers who have set up encryption, most recently active first, that a
    /// member creating a key seals it to right away.
    /// </summary>
    public async Task<List<long>> GetKeyCandidatesAsync(Channel channel, long exceptUserId, int limit)
    {
        var viewers = await GetViewerUserIdsAsync(channel);
        if (viewers.Count == 0)
            return [];

        // A community node picks from the key logs it already has, plus
        // viewers whose logs it has not fetched from the hub recently, and
        // only asks the hub about the ones it picks. Asking about every
        // viewer of a large planet would be one hub request per 500 members.
        var pool = await _identity.GetUsersWithIdentityAsync(viewers, syncFromHub: false);
        pool.UnionWith(E2eeIdentityService.NotSyncedFromHub(viewers.Where(v => !pool.Contains(v))));
        pool.Remove(exceptUserId);
        if (pool.Count == 0)
            return [];

        var chosen = await _db.Users.AsNoTracking()
            .Where(x => pool.Contains(x.Id))
            .OrderByDescending(x => x.TimeLastActive)
            .Select(x => x.Id)
            .Take(Math.Clamp(limit, 1, E2eeLimits.MaxKeyCandidates))
            .ToListAsync();
        if (!FederationNodeService.NodeEnabled)
            return chosen;

        var withIdentity = await _identity.GetUsersWithIdentityAsync(chosen);
        return chosen.Where(withIdentity.Contains).ToList();
    }

    // Server-created keys

    /// <summary>
    /// Returns the channel's newest key generation, creating the first one on
    /// the server when the channel has none. The server needs a key to seal
    /// webhook and system messages and earlier plain-text history.
    ///
    /// The key is sealed to recently active viewers who have set up
    /// encryption. Until enough members hold it (see
    /// <see cref="ForgetHeldKeyIfSharedAsync"/>), the server also keeps a copy
    /// protected with Data Protection and delivers it to viewers who load the
    /// channel's keys. Members replace it before sending, so it only ever
    /// protects text the server already had.
    /// </summary>
    public async Task<Valour.Database.E2eeChannelKeyGeneration> EnsureSealingGenerationAsync(Channel channel)
    {
        var latest = await GetLatestGenerationAsync(channel.Id);
        if (latest > 0)
            return await GetGenerationAsync(channel.Id, latest);

        var secret = ChannelKeySecret.Generate(channel.Id, 1);
        var (keyId, sign) = await _serverKeys.GetSignerAsync();
        var (policy, sharesHistory) = await GetPolicyAsync(channel);
        var (entry, record) = ChannelKeyGenerationBuilder.CreateByServer(secret, channel.PlanetId ?? 0, keyId, sign,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ChannelKeyTerms.Ungoverned(policy, sharesHistory));

        var boxes = await SealToRecentViewersAsync(channel, secret);
        var now = DateTime.UtcNow;
        var generation = new Valour.Database.E2eeChannelKeyGeneration
        {
            ChannelId = channel.Id,
            Generation = 1,
            Body = entry.Body,
            Signature = entry.Signature,
            CreatorUserId = ChannelKeyGenerationRecord.ServerCreatorId,
            SealPublicKey = record.SealPublicKey,
            IndexGeneration = 1,
            HeldSecretProtected = boxes.Count < HeldKeyMinimumHolders
                ? _serverKeys.ProtectHeldSecret(secret.Secret)
                : null,
            CreatedAt = now
        };
        _db.E2eeChannelKeyGenerations.Add(generation);
        AddBoxes(boxes, ChannelKeyGenerationRecord.ServerCreatorId, now);

        // A member or another server request may have published the first key.
        if (!await SaveAndAdvanceGenerationAsync(channel.Id, 0, 1))
            return await GetGenerationAsync(channel.Id, await GetLatestGenerationAsync(channel.Id));

        _db.Entry(generation).State = EntityState.Detached;
        if (generation.HeldSecretProtected is not null)
            await ForgetHeldKeyIfSharedAsync(channel);
        await PublishGenerationAsync(channel, 1);
        return generation;
    }

    /// <summary>
    /// Saves the pending generation and boxes and advances the channel's
    /// generation from <paramref name="expected"/> to <paramref name="next"/>
    /// in one transaction. Advancing only when the channel still holds the
    /// expected value stops two members from publishing the same generation.
    /// Returns false, with nothing saved, when another request got there first.
    /// </summary>
    private async Task<bool> SaveAndAdvanceGenerationAsync(long channelId, int expected, int next)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.SaveChangesAsync();
            var updated = await _db.Channels
                .Where(x => x.Id == channelId && x.EncryptionGeneration == expected)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.EncryptionGeneration, next));
            if (updated == 1)
            {
                await transaction.CommitAsync();
                return true;
            }
        }
        catch (DbUpdateException)
        {
            // Another request stored the same generation first.
        }

        await transaction.RollbackAsync();
        _db.ChangeTracker.Clear();
        return false;
    }

    private async Task<List<ChannelKeyBoxDto>> SealToRecentViewersAsync(Channel channel, ChannelKeySecret secret)
    {
        var recent = await GetKeyCandidatesAsync(channel, ChannelKeyGenerationRecord.ServerCreatorId, E2eeLimits.MaxBoxesPerRequest);
        if (recent.Count == 0)
            return [];

        var admitted = await GetAdmittedFilterAsync(channel);
        var boxes = new List<ChannelKeyBoxDto>();
        foreach (var (userId, state) in await _identity.GetStatesAsync(recent))
        {
            if (!admitted(userId, state))
                continue;

            boxes.Add(new ChannelKeyBoxDto
            {
                ChannelId = channel.Id,
                Generation = secret.Generation,
                UserId = userId,
                UserKeyGeneration = state.UserKey.Generation,
                Box = secret.SealTo(userId, state.UserKey)
            });
        }

        return boxes;
    }

    /// <summary>
    /// In governed channels, only users the access log admits may hold keys.
    /// A group without a log admits nobody yet.
    /// </summary>
    private async Task<Func<long, UserKeyState, bool>> GetAdmittedFilterAsync(Channel channel)
    {
        var (policy, _) = await GetPolicyAsync(channel);
        if (!IsGoverned(policy))
            return (_, _) => true;

        var log = await GetGoverningAccessLogAsync(channel, policy);
        if (log is null)
            return (_, _) => false;
        return (userId, state) => log.IsMember(userId, state);
    }

    private static bool IsGoverned(ChannelKeyPolicy policy) =>
        policy is ChannelKeyPolicy.PlanetInviteOnly or ChannelKeyPolicy.Group;

    /// <summary>
    /// In governed channels, only members the access log admits with their
    /// current keys may create or share channel keys. Members' devices refuse
    /// keys from anyone else, so this stops a key nobody trusts from blocking
    /// the channel.
    /// </summary>
    private async Task<TaskResult> RequireAdmittedAsync(Channel channel, long userId, UserKeyState state)
    {
        var (policy, _) = await GetPolicyAsync(channel);
        if (!IsGoverned(policy))
            return TaskResult.SuccessResult;

        var log = await GetGoverningAccessLogAsync(channel, policy);
        if (log is null)
            return TaskResult.FromFailure("Start the membership log before sharing keys.");
        return log.IsMember(userId, state)
            ? TaskResult.SuccessResult
            : TaskResult.FromFailure("You have not been admitted to this channel's encryption.");
    }

    /// <summary>
    /// Seals a key the server is still holding to a viewer who can now
    /// receive it and does not already reach it through a later key. In a
    /// planet that hides history, the key only goes to viewers while it is
    /// still the channel's newest key.
    /// </summary>
    private async Task DeliverHeldKeyAsync(Channel channel, long userId, int latest, bool sharesHistory)
    {
        if (latest > 1 && !sharesHistory)
            return;

        var held = await _db.E2eeChannelKeyGenerations.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Generation == 1 && x.HeldSecretProtected != null)
            .Select(x => x.HeldSecretProtected)
            .FirstOrDefaultAsync();
        if (held is null)
            return;

        // A planet being migrated is read-only until its copy is handed off.
        if (!(await MigrationLock.GuardAsync(_db, channel.PlanetId)).Success)
            return;

        var userGenerations = (await GetOpenableGenerationsAsync(channel.Id, [userId])).GetValueOrDefault(userId) ?? [];
        if (ChannelKeyChain.Reachable(await GetLinksAsync(channel.Id), userGenerations).Contains(1))
            return;
        if (!await CanViewAsync(channel, userId))
            return;

        var state = await _identity.GetStateAsync(userId);
        if (state?.UserKey is null || !(await GetAdmittedFilterAsync(channel))(userId, state))
            return;

        // A key ring that no longer matches, for example after a database
        // restore without the same KEK, only stops the held key from being
        // delivered; loading the channel's other keys still works.
        byte[] heldSecret;
        try
        {
            heldSecret = _serverKeys.UnprotectHeldSecret(held);
        }
        catch (CryptographicException e)
        {
            _logger.LogError(e, "The held key of channel {ChannelId} could not be unprotected", channel.Id);
            return;
        }

        var secret = new ChannelKeySecret(channel.Id, 1, heldSecret);

        // A box the user can no longer open, from before a key reset, is replaced.
        await _db.E2eeChannelKeyBoxes
            .Where(x => x.ChannelId == channel.Id && x.UserId == userId && x.Generation == 1)
            .ExecuteDeleteAsync();
        var inserted = await InsertBoxesAsync(channel.Id, [new ChannelKeyBoxDto
        {
            ChannelId = channel.Id,
            Generation = 1,
            UserId = userId,
            UserKeyGeneration = state.UserKey.Generation,
            Box = secret.SealTo(userId, state.UserKey)
        }], ChannelKeyGenerationRecord.ServerCreatorId, DateTime.UtcNow);
        if (inserted.Count == 0)
            return;

        await ForgetHeldKeyIfSharedAsync(channel);
        await RemoveFulfilledRequestsAsync(channel, [userId]);
    }

    /// <summary>
    /// Members who must hold a server-created key before the server forgets
    /// its copy. One member alone could lose it by resetting their keys.
    /// </summary>
    private static int HeldKeyMinimumHolders => Math.Max(1, E2eeConfig.Current.HeldKeyMinimumHolders);

    /// <summary>
    /// Forgets the server's copy of a server-created first key once enough
    /// members can open it: distinct members holding it, or holding a later
    /// generation that unlocks it, number at least
    /// <see cref="HeldKeyMinimumHolders"/>, or every viewer in a smaller channel.
    /// </summary>
    private async Task ForgetHeldKeyIfSharedAsync(Channel channel)
    {
        var isHeld = await _db.E2eeChannelKeyGenerations.AnyAsync(x =>
            x.ChannelId == channel.Id && x.Generation == 1 && x.HeldSecretProtected != null);
        if (!isHeld)
            return;

        // Generation 1 is reachable from every generation up to the first one
        // that does not unlock its predecessor.
        var links = await GetLinksAsync(channel.Id);
        var lastLinked = 1;
        while (links.TryGetValue(lastLinked + 1, out var link) && link.UnlocksPrevious)
            lastLinked++;

        // Only boxes their holders can still open count. A box sealed to a
        // user key from before a reset is useless to that user.
        var holderIds = await _db.E2eeChannelKeyBoxes.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Generation <= lastLinked)
            .Select(x => x.UserId)
            .Distinct()
            .Take(E2eeLimits.MaxBoxesPerRequest)
            .ToListAsync();
        var holders = (await GetOpenableGenerationsAsync(channel.Id, holderIds))
            .Count(h => h.Value.Any(g => g <= lastLinked));

        var required = HeldKeyMinimumHolders;
        if (holders < required)
            required = Math.Min(required, Math.Max(1, (await GetViewerUserIdsAsync(channel)).Count));
        if (holders < required)
            return;

        await _db.E2eeChannelKeyGenerations
            .Where(x => x.ChannelId == channel.Id && x.HeldSecretProtected != null)
            .ExecuteUpdateAsync(x => x.SetProperty(g => g.HeldSecretProtected, (string)null));
    }

    /// <summary>
    /// Updates caches and tells viewers that the channel's key changed. The
    /// channel update also carries the new generation to every viewer.
    /// </summary>
    private async Task PublishGenerationAsync(Channel channel, int generation)
    {
        channel.EncryptionGeneration = generation;

        if (channel.PlanetId is not null)
        {
            var hosted = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
            var cached = hosted.GetChannel(channel.Id);
            if (cached is not null)
                cached.EncryptionGeneration = generation;

            var viewers = await _permissionService.GetChannelViewerUserIdsAsync(hosted, [channel.Id]);
            _coreHub.NotifyChannelChange(cached ?? channel, viewers[0]);
            await NotifyKeysAvailableAsync(channel, []);
            return;
        }

        var dbChannel = await _db.Channels.AsNoTracking().Include(x => x.Members)
            .FirstAsync(x => x.Id == channel.Id);
        var memberIds = dbChannel.Members.Select(x => x.UserId).ToList();
        await _coreHub.RelayDirectChannelUpdate(dbChannel.ToModel(), _nodeLifecycleService, memberIds);
        await NotifyKeysAvailableAsync(channel, memberIds);
    }

    // Boxes

    /// <summary>
    /// Stores boxes a member sealed for others. Boxes sealed to a user key
    /// the recipient has since replaced are skipped and listed in the result,
    /// so the sharer can reload those users' keys and share again.
    /// </summary>
    public async Task<TaskResult<ChannelKeyShareResultDto>> ShareBoxesAsync(Channel channel, long userId,
        List<ChannelKeyBoxDto> boxes)
    {
        var shareResult = new ChannelKeyShareResultDto();
        if (boxes is null || boxes.Count == 0)
            return TaskResult<ChannelKeyShareResultDto>.FromData(shareResult);
        if (boxes.Count > E2eeLimits.MaxBoxesPerRequest)
            return TaskResult<ChannelKeyShareResultDto>.FromFailure("Too many key boxes.");

        var migrationLock = await MigrationLock.GuardAsync(_db, channel.PlanetId);
        if (!migrationLock.Success)
            return TaskResult<ChannelKeyShareResultDto>.FromFailure(migrationLock.Message);

        if (!await CanViewAsync(channel, userId))
            return TaskResult<ChannelKeyShareResultDto>.FromFailure("You cannot view this channel.");

        var sharerState = await _identity.GetStateAsync(userId);
        if (sharerState is null)
            return TaskResult<ChannelKeyShareResultDto>.FromFailure("Set up encryption first.");
        var sharerAdmitted = await RequireAdmittedAsync(channel, userId, sharerState);
        if (!sharerAdmitted.Success)
            return TaskResult<ChannelKeyShareResultDto>.FromFailure(sharerAdmitted.Message);

        // A member holds a generation directly or through a later one that
        // unlocks it.
        var generations = boxes.Select(b => b.Generation).Distinct().ToList();
        var held = await _db.E2eeChannelKeyBoxes.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.UserId == userId)
            .Select(x => x.Generation)
            .ToListAsync();
        var reachable = ChannelKeyChain.Reachable(await GetLinksAsync(channel.Id), held);
        if (generations.Any(g => !reachable.Contains(g)))
            return TaskResult<ChannelKeyShareResultDto>.FromFailure("You can only share keys you hold.");

        // Every generation's boxes are checked together, so recipients' keys,
        // the access log, and viewing rights are each loaded once.
        var result = await ValidateBoxesAsync(channel, boxes);
        if (!result.Success)
            return TaskResult<ChannelKeyShareResultDto>.FromFailure(result.Message);
        shareResult.StaleRecipients = result.Data.Stale;

        // The first box for a member wins. Boxes another member stored first,
        // even at the same moment, are skipped and the rest are still stored.
        var recipients = await InsertBoxesAsync(channel.Id, result.Data.Valid, userId, DateTime.UtcNow);
        if (recipients.Count == 0)
            return TaskResult<ChannelKeyShareResultDto>.FromData(shareResult);

        await RemoveFulfilledRequestsAsync(channel, recipients);
        await ForgetHeldKeyIfSharedAsync(channel);
        await NotifyKeysAvailableAsync(channel, recipients);
        return TaskResult<ChannelKeyShareResultDto>.FromData(shareResult);
    }

    /// <summary>
    /// Stores boxes, skipping any member who already has a box for that
    /// generation. Returns the members who received at least one new box.
    /// </summary>
    private async Task<List<long>> InsertBoxesAsync(long channelId, List<ChannelKeyBoxDto> boxes, long sharedBy,
        DateTime now)
    {
        if (boxes.Count == 0)
            return [];

        var inserted = await _db.Database.SqlQueryRaw<long>(
            """
            INSERT INTO e2ee_channel_key_boxes
                (channel_id, generation, user_id, user_key_generation, box, shared_by_user_id, created_at)
            SELECT @channel_id, t.generation, t.user_id, t.user_key_generation, t.box, @shared_by, @now
            FROM unnest(@generations, @user_ids, @user_key_generations, @boxes)
                AS t(generation, user_id, user_key_generation, box)
            ON CONFLICT DO NOTHING
            RETURNING user_id AS "Value"
            """,
            new NpgsqlParameter<long>("channel_id", channelId),
            new NpgsqlParameter<long>("shared_by", sharedBy),
            new NpgsqlParameter<DateTime>("now", DateTime.SpecifyKind(now, DateTimeKind.Utc)),
            new NpgsqlParameter<int[]>("generations", boxes.Select(b => b.Generation).ToArray()),
            new NpgsqlParameter<long[]>("user_ids", boxes.Select(b => b.UserId).ToArray()),
            new NpgsqlParameter<int[]>("user_key_generations", boxes.Select(b => b.UserKeyGeneration).ToArray()),
            new NpgsqlParameter("boxes", NpgsqlDbType.Array | NpgsqlDbType.Bytea)
            {
                Value = boxes.Select(b => b.Box).ToArray()
            }).ToListAsync();

        return inserted.Distinct().ToList();
    }

    /// <summary>Boxes that can be stored, and recipients whose boxes were sealed to a replaced user key.</summary>
    private sealed record BoxValidation(List<ChannelKeyBoxDto> Valid, List<long> Stale);

    /// <summary>
    /// Checks boxes for this channel. A box sealed to a user key the
    /// recipient has since replaced, usually because they signed out or reset
    /// on a device a moment ago, is set aside rather than failing the rest.
    /// Anything else wrong with a box fails the whole request.
    /// </summary>
    private async Task<TaskResult<BoxValidation>> ValidateBoxesAsync(Channel channel, List<ChannelKeyBoxDto> boxes)
    {
        if (boxes.DistinctBy(b => (b.UserId, b.Generation)).Count() != boxes.Count)
            return TaskResult<BoxValidation>.FromFailure("Each member can receive one box per key.");

        var recipientIds = boxes.Select(b => b.UserId).Distinct().ToList();
        var states = await _identity.GetStatesAsync(recipientIds);

        // Clients refuse to share keys with anyone the access log does not
        // admit. The server applies the same rule so a modified client cannot
        // hand keys to an outsider through it.
        AccessLogState accessLog = null;
        var (policy, _) = await GetPolicyAsync(channel);
        if (IsGoverned(policy))
        {
            accessLog = await GetGoverningAccessLogAsync(channel, policy);
            if (accessLog is null)
                return TaskResult<BoxValidation>.FromFailure("Start the membership log before sharing keys.");
        }

        var canView = new Dictionary<long, bool>();
        var valid = new List<ChannelKeyBoxDto>();
        var stale = new HashSet<long>();
        foreach (var box in boxes)
        {
            if (box.ChannelId != channel.Id)
                return TaskResult<BoxValidation>.FromFailure("Key box is for a different key.");
            if (box.Box is null || box.Box.Length > 512)
                return TaskResult<BoxValidation>.FromFailure("Invalid key box.");
            if (!states.TryGetValue(box.UserId, out var state))
                return TaskResult<BoxValidation>.FromFailure("A recipient has not set up encryption.");

            // Checked first, because a recipient who reset their keys may
            // also need admitting again, and the sharer learns both on reload.
            if (box.UserKeyGeneration != state.UserKey.Generation)
            {
                stale.Add(box.UserId);
                continue;
            }

            if (accessLog is not null && !accessLog.IsMember(box.UserId, state))
                return TaskResult<BoxValidation>.FromFailure("A recipient has not been admitted.");

            if (!canView.TryGetValue(box.UserId, out var viewer))
                canView[box.UserId] = viewer = await CanViewAsync(channel, box.UserId);
            if (!viewer)
                return TaskResult<BoxValidation>.FromFailure("A recipient cannot view this channel.");

            valid.Add(box);
        }

        return TaskResult<BoxValidation>.FromData(new BoxValidation(valid, stale.ToList()));
    }

    private void AddBoxes(IEnumerable<ChannelKeyBoxDto> boxes, long sharedBy, DateTime now)
    {
        foreach (var box in boxes)
        {
            _db.E2eeChannelKeyBoxes.Add(new Valour.Database.E2eeChannelKeyBox
            {
                ChannelId = box.ChannelId,
                Generation = box.Generation,
                UserId = box.UserId,
                UserKeyGeneration = box.UserKeyGeneration,
                Box = box.Box,
                SharedByUserId = sharedBy,
                CreatedAt = now
            });
        }
    }

    // Requests

    public async Task<ChannelKeyRecipientsDto> GetRecipientsAsync(Channel channel)
    {
        var latest = await GetLatestGenerationAsync(channel.Id);
        var result = new ChannelKeyRecipientsDto { ChannelId = channel.Id, LatestGeneration = latest };
        if (latest == 0)
            return result;

        var latestRow = await _db.E2eeChannelKeyGenerations.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Generation == latest)
            .Select(x => new { x.HasMessages })
            .FirstAsync();
        var links = await GetLinksAsync(channel.Id);
        result.LatestIsUnused = !latestRow.HasMessages && !links[latest].UnlocksPrevious;

        // The channel's encryption details list who holds the key. Serving
        // requests does not need the list, so a large channel sends only the
        // most recent holders and a count.
        var holderQuery = _db.E2eeChannelKeyBoxes.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Generation == latest);
        result.HolderCount = await holderQuery.CountAsync();
        result.Holders = await holderQuery
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.UserId)
            .Take(ChannelKeyRecipientsDto.MaxListedHolders)
            .ToListAsync();

        List<long> candidates;
        if (channel.PlanetId is null)
        {
            candidates = await GetViewerUserIdsAsync(channel);
        }
        else
        {
            candidates = await _db.E2eeKeyRequests.AsNoTracking()
                .Where(x => x.ChannelId == channel.Id)
                .OrderBy(x => x.RequestedAt)
                .Take(E2eeLimits.MaxBoxesPerRequest)
                .Select(x => x.UserId)
                .ToListAsync();
        }

        // Holders of the newest key can still be missing an earlier chain, for
        // example a member who started a key because nobody could share one.
        var missing = await GetMissingGenerationsAsync(channel, latest, candidates);
        candidates = candidates.Where(c => missing.GetValueOrDefault(c) is { Count: > 0 }).ToList();
        if (candidates.Count == 0)
            return result;

        var withIdentity = await _identity.GetUsersWithIdentityAsync(candidates);
        // Any box counts here, even one from before a key reset: it shows the
        // user was given this channel's keys before.
        var withEarlierKeys = await _db.E2eeChannelKeyBoxes.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && candidates.Contains(x.UserId))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync();
        var earlier = withEarlierKeys.ToHashSet();

        foreach (var userId in candidates)
        {
            if (!withIdentity.Contains(userId) || !await CanViewAsync(channel, userId))
                continue;
            result.Pending.Add(new PendingKeyRecipientDto
            {
                UserId = userId,
                HasEarlierKeys = earlier.Contains(userId),
                Generations = missing[userId]
            });
        }

        return result;
    }

    public async Task<TaskResult> RequestKeysAsync(Channel channel, long userId)
    {
        if (!await CanViewAsync(channel, userId))
            return TaskResult.FromFailure("You cannot view this channel.");
        if (await _identity.GetStateAsync(userId) is null)
            return TaskResult.FromFailure("Set up encryption first.");

        // Members were just asked; asking again would only repeat the same
        // notifications to everyone watching the channel.
        var now = DateTime.UtcNow;
        var existing = await _db.E2eeKeyRequests.FindAsync(channel.Id, userId);
        if (existing is not null && now - existing.RequestedAt < KeyRequestRepeatInterval)
            return TaskResult.SuccessResult;

        if (existing is null)
        {
            _db.E2eeKeyRequests.Add(new Valour.Database.E2eeKeyRequest
            {
                ChannelId = channel.Id,
                UserId = userId,
                RequestedAt = now
            });
        }
        else
        {
            existing.RequestedAt = now;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
        }

        var evt = new E2eeRealtimeEvent
        {
            Type = E2eeRealtimeEventTypes.KeyRequest,
            ChannelId = channel.Id,
            PlanetId = channel.PlanetId,
            UserId = userId
        };

        if (channel.PlanetId is not null)
        {
            await _realtime.SendToChannelAsync(channel.Id, evt);

            // Members who hold the key but do not have the channel open are
            // asked too, so a quiet channel does not leave the newcomer to
            // start a key of their own. The most recent holders are the most
            // likely to be online.
            var latest = await GetLatestGenerationAsync(channel.Id);
            var holders = await _db.E2eeChannelKeyBoxes.AsNoTracking()
                .Where(x => x.ChannelId == channel.Id && x.Generation == latest && x.UserId != userId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => x.UserId)
                .Take(MaxNotifiedHolders)
                .ToListAsync();
            foreach (var holder in holders)
                await _realtime.SendToUserAsync(holder, evt);
        }
        else
        {
            var members = await GetViewerUserIdsAsync(channel);
            foreach (var member in members.Where(m => m != userId))
                await _realtime.SendToUserAsync(member, evt);
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Requests in direct and group chats where the user holds a key, so a
    /// client that comes online can serve people who asked while it was away.
    /// </summary>
    public async Task<List<KeyRequestDto>> GetDirectRequestsForHolderAsync(long userId)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromDays(30);

        // Starts from the channels the holder has boxes in (indexed by user),
        // and only lists requests from people who are still members, to
        // holders who are still members.
        var heldChannels = _db.E2eeChannelKeyBoxes.AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => b.ChannelId)
            .Distinct();
        var requests = await (
            from request in _db.E2eeKeyRequests.AsNoTracking()
            join channel in _db.Channels.AsNoTracking() on request.ChannelId equals channel.Id
            where heldChannels.Contains(request.ChannelId) &&
                  channel.PlanetId == null && request.UserId != userId && request.RequestedAt > cutoff &&
                  _db.ChannelMembers.Any(m => m.ChannelId == request.ChannelId && m.UserId == request.UserId) &&
                  _db.ChannelMembers.Any(m => m.ChannelId == request.ChannelId && m.UserId == userId)
            orderby request.RequestedAt
            select new KeyRequestDto
            {
                ChannelId = request.ChannelId
            }).Take(E2eeLimits.MaxBoxesPerRequest).ToListAsync();

        return requests;
    }

    /// <summary>
    /// Removes the requests of users who now hold every key they may receive.
    /// Someone still missing an earlier chain keeps their request, so members
    /// who hold it share it when they come online.
    /// </summary>
    private async Task RemoveFulfilledRequestsAsync(Channel channel, IEnumerable<long> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return;

        var latest = await GetLatestGenerationAsync(channel.Id);
        var missing = await GetMissingGenerationsAsync(channel, latest, ids);
        var fulfilled = ids.Where(id => missing.GetValueOrDefault(id) is not { Count: > 0 }).ToList();
        if (fulfilled.Count == 0)
            return;

        await _db.E2eeKeyRequests
            .Where(x => x.ChannelId == channel.Id && fulfilled.Contains(x.UserId))
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// Tells a planet channel's viewers, or the given members of a direct
    /// channel, that new keys are available.
    /// </summary>
    private async Task NotifyKeysAvailableAsync(Channel channel, List<long> recipients)
    {
        var evt = new E2eeRealtimeEvent
        {
            Type = E2eeRealtimeEventTypes.KeysAvailable,
            ChannelId = channel.Id,
            PlanetId = channel.PlanetId
        };

        if (channel.PlanetId is not null)
        {
            await _realtime.SendToChannelAsync(channel.Id, evt);
            return;
        }

        foreach (var recipient in recipients)
            await _realtime.SendToUserAsync(recipient, evt);
    }
}
