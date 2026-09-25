using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Valour.Sdk.E2ee;
using Valour.Server.Hubs;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Stores and verifies access logs: the signed membership records of private
/// planets and group DMs. Clients share channel keys only with users these
/// logs admit, which is what stops the server from adding anyone.
/// </summary>
public class E2eeAccessLogService
{
    /// <summary>
    /// Entries a checkpoint may follow. A log gets a new checkpoint no more
    /// often than this, so checkpoints stay a small share of it.
    /// </summary>
    public const int MinCheckpointInterval = 16;

    /// <summary>
    /// Largest entry that carries the whole membership (a start, restart, or
    /// checkpoint), which grows with the number of members.
    /// </summary>
    public const int MaxStateEntrySize = 1024 * 1024;

    /// <summary>
    /// Largest entry that adds or removes members, about 9,000 added members
    /// (each names their key epoch and key ID) or 30,000 removed ones. Larger
    /// changes need several entries.
    /// </summary>
    public const int MaxMembershipEntrySize = 256 * 1024;

    /// <summary>Largest entry of any other type, such as an invite or an admin change.</summary>
    public const int MaxEntrySize = 64 * 1024;

    /// <summary>
    /// Entry bytes one response carries. A device that receives fewer entries
    /// than exist continues from the last one on its next refresh.
    /// </summary>
    public const int MaxResponseBytes = 8 * 1024 * 1024;

    /// <summary>Scopes whose verified state is cached before the cache starts over.</summary>
    private const int MaxCachedStates = 20_000;

    // Verified states by scope. Entries are append-only, so a cached state is
    // brought up to date by applying only the entries after its head.
    private static readonly ConcurrentDictionary<(AccessLogScope, long), AccessLogState> StateCache = new();

    private readonly ValourDb _db;
    private readonly E2eeIdentityService _identity;
    private readonly E2eeRealtimeService _realtime;
    private readonly IHubContext<CoreHub> _hub;
    private readonly ILogger<E2eeAccessLogService> _logger;

    public E2eeAccessLogService(ValourDb db, E2eeIdentityService identity, E2eeRealtimeService realtime,
        IHubContext<CoreHub> hub, ILogger<E2eeAccessLogService> logger)
    {
        _db = db;
        _identity = identity;
        _realtime = realtime;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// Returns entries from <paramref name="fromSeq"/> onward. A caller with
    /// nothing yet receives entries from the latest checkpoint unless it asks
    /// for the full log. With <paramref name="maxBytes"/>, entries stop once
    /// that many bytes are included, always returning at least one entry.
    /// </summary>
    public async Task<List<AccessLogEntry>> GetEntriesAsync(AccessLogScope scope, long scopeId, int fromSeq = 0,
        bool full = true, int? maxBytes = null)
    {
        if (fromSeq == 0 && !full)
        {
            fromSeq = await _db.E2eeAccessLogEntries.AsNoTracking()
                .Where(x => x.Scope == (int)scope && x.ScopeId == scopeId && x.IsCheckpoint)
                .MaxAsync(x => (int?)x.Seq) ?? 0;
        }

        var query = _db.E2eeAccessLogEntries.AsNoTracking()
            .Where(x => x.Scope == (int)scope && x.ScopeId == scopeId && x.Seq >= fromSeq)
            .OrderBy(x => x.Seq);

        var entries = new List<AccessLogEntry>();
        long bytes = 0;
        await foreach (var row in query.AsAsyncEnumerable())
        {
            bytes += row.Body.Length + row.Signature.Length;
            if (maxBytes is not null && bytes > maxBytes && entries.Count > 0)
                break;

            entries.Add(new AccessLogEntry
            {
                Scope = scope,
                ScopeId = row.ScopeId,
                Seq = row.Seq,
                Body = row.Body,
                Signature = row.Signature
            });
        }

        return entries;
    }

    /// <summary>
    /// True when the user's current keys, as their verified key log shows
    /// them, are the ones a log names as its owner, so their device can sign
    /// entries only the owner may sign.
    /// </summary>
    public async Task<bool> IsOwnerWithCurrentKeysAsync(AccessLogState log, long userId) =>
        log.IsOwner(userId, await _identity.GetStateAsync(userId));

    public async Task<bool> ExistsAsync(AccessLogScope scope, long scopeId) =>
        await _db.E2eeAccessLogEntries.AnyAsync(x => x.Scope == (int)scope && x.ScopeId == scopeId);

    /// <summary>
    /// Returns the verified log state, or null if the scope has no log. The
    /// server checked every checkpoint against its replay when it was
    /// appended, so it starts from its own latest checkpoint.
    /// </summary>
    public async Task<AccessLogState> GetStateAsync(AccessLogScope scope, long scopeId)
    {
        var head = await _db.E2eeAccessLogEntries.AsNoTracking()
            .Where(x => x.Scope == (int)scope && x.ScopeId == scopeId)
            .MaxAsync(x => (int?)x.Seq);
        if (head is null)
        {
            StateCache.TryRemove((scope, scopeId), out _);
            return null;
        }

        StateCache.TryGetValue((scope, scopeId), out var cached);
        if (cached is not null && cached.HeadSeq == head)
            return cached;

        try
        {
            AccessLogState state;
            if (cached is not null && cached.HeadSeq < head)
            {
                var newer = await GetEntriesAsync(scope, scopeId, cached.HeadSeq + 1);
                var states = await _identity.GetStatesAsync(AccessLogVerifier.RequiredUsers(newer));
                state = AccessLogState.Decode(cached.Encode());
                foreach (var entry in newer)
                    AccessLogVerifier.Apply(state, entry, states);
            }
            else
            {
                var entries = await GetEntriesAsync(scope, scopeId, full: false);
                var states = await _identity.GetStatesAsync(AccessLogVerifier.RequiredUsers(entries));

                // A checkpoint a removed device signed after its cutoff cannot
                // start the log, so the whole log is read instead.
                if (!AccessLogVerifier.CanStartFrom(entries, states))
                {
                    entries = await GetEntriesAsync(scope, scopeId, full: true);
                    states = await _identity.GetStatesAsync(AccessLogVerifier.RequiredUsers(entries));
                }

                state = AccessLogVerifier.Verify(scope, scopeId, entries, states);
            }

            E2eeCacheLimit.Trim(StateCache, MaxCachedStates);
            StateCache[(scope, scopeId)] = state;
            return state;
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            _logger.LogError(e, "Stored access log {Scope}/{ScopeId} does not verify", scope, scopeId);
            return null;
        }
    }

    /// <summary>
    /// Verifies and appends an entry. A planet's genesis, restart, and open
    /// entries are appended through <see cref="PlanetEncryptionService"/>,
    /// which also makes the planet private or public, and its ownership
    /// transfer entry through <see cref="PlanetService.TransferOwnershipAsync"/>,
    /// which also changes the planet's owner. Pass
    /// <paramref name="allowStart"/> from those paths and for group DMs,
    /// which start their logs directly.
    /// </summary>
    public async Task<TaskResult<AccessLogState>> AppendAsync(AccessLogScope scope, long scopeId, long userId,
        AccessLogEntry entry, bool allowStart = false, bool allowOwnershipTransfer = false)
    {
        if (entry?.Body is null || entry.Signature is null)
            return TaskResult<AccessLogState>.FromFailure("Include the access log entry.");
        if (entry.Scope != scope || entry.ScopeId != scopeId)
            return TaskResult<AccessLogState>.FromFailure("The entry belongs to a different log.");
        if (entry.Body.Length > MaxStateEntrySize || entry.Signature.Length > 1024)
            return TaskResult<AccessLogState>.FromFailure("Access log entry is too large.");

        AccessLogRecord record;
        try
        {
            record = AccessLogRecord.Decode(entry.Body);
        }
        catch (E2eeFormatException e)
        {
            return TaskResult<AccessLogState>.FromFailure(e.Message);
        }

        if (record.SignerUserId != userId)
            return TaskResult<AccessLogState>.FromFailure("You can only submit entries you signed.");
        var maxSize = record.Type switch
        {
            AccessLogEntryType.Genesis or AccessLogEntryType.Restart or AccessLogEntryType.Checkpoint => MaxStateEntrySize,
            AccessLogEntryType.AddMembers or AccessLogEntryType.RemoveMembers => MaxMembershipEntrySize,
            _ => MaxEntrySize
        };
        if (entry.Body.Length > maxSize)
            return TaskResult<AccessLogState>.FromFailure("Access log entry is too large. Split the change into smaller entries.");

        // Entry times never go backwards, so one dated far ahead would force
        // every later entry into the future and break invite expiry. One
        // dated far back could redeem an invite after it expired.
        var now = DateTimeOffset.UtcNow;
        if (record.TimestampMs > now.AddMinutes(10).ToUnixTimeMilliseconds())
            return TaskResult<AccessLogState>.FromFailure("The entry is dated in the future. Check this device's clock.");
        if (record.TimestampMs < now.AddMinutes(-10).ToUnixTimeMilliseconds())
            return TaskResult<AccessLogState>.FromFailure("The entry is dated in the past. Check this device's clock.");
        if (!allowStart && record.Type is AccessLogEntryType.Genesis or AccessLogEntryType.Restart
                or AccessLogEntryType.Open)
            return TaskResult<AccessLogState>.FromFailure(
                "Change whether the planet is public in its privacy settings to start or end its membership log.");

        // A planet's log owner changes only together with the planet's owner.
        if (!allowOwnershipTransfer && scope == AccessLogScope.Planet &&
            record.Type == AccessLogEntryType.TransferOwnership)
            return TaskResult<AccessLogState>.FromFailure("Transfer the planet's ownership to change its membership log's owner.");

        var policy = await CheckScopeAsync(scope, scopeId, userId, record);
        if (!policy.Success)
            return TaskResult<AccessLogState>.FromFailure(policy.Message);

        var current = await GetStateAsync(scope, scopeId);
        if (current is null && await ExistsAsync(scope, scopeId))
            return TaskResult<AccessLogState>.FromFailure("The membership log could not be verified.");

        if (record.Type == AccessLogEntryType.Checkpoint && current is not null &&
            current.HeadSeq - Math.Max(current.LastCheckpointSeq, 0) < MinCheckpointInterval)
            return TaskResult<AccessLogState>.FromFailure("The membership log had a checkpoint recently.");

        // Held until the entry is stored, so the signer's device cannot be
        // removed between the check below and the insert.
        await using var keysLock = await _identity.LockUserKeysAsync(record.SignerUserId);

        AccessLogState state;
        try
        {
            var states = await _identity.GetStatesAsync([record.SignerUserId]);

            // Other devices accept an entry dated before its signer's removal,
            // so the server takes new entries only from devices active now.
            if (states.GetValueOrDefault(record.SignerUserId)?.GetActiveSigner(record.SignerDeviceId) is null)
                return TaskResult<AccessLogState>.FromFailure("This device was removed from your account.");

            state = current is null
                ? new AccessLogState { Scope = scope, ScopeId = scopeId }
                : AccessLogState.Decode(current.Encode());
            AccessLogVerifier.Apply(state, entry, states);
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return TaskResult<AccessLogState>.FromFailure(e.Message);
        }

        _db.E2eeAccessLogEntries.Add(new Valour.Database.E2eeAccessLogEntry
        {
            Scope = (int)scope,
            ScopeId = scopeId,
            Seq = entry.Seq,
            Body = entry.Body,
            Signature = entry.Signature,
            SignerUserId = userId,
            IsCheckpoint = record.Type == AccessLogEntryType.Checkpoint,
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
            if (keysLock is not null)
                await keysLock.CommitAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return TaskResult<AccessLogState>.FromFailure(
                $"{E2eeErrorCodes.AccessLogChanged}: The membership log changed. Refresh and try again.");
        }

        StateCache[(scope, scopeId)] = state;
        await NotifyAsync(scope, scopeId);
        return TaskResult<AccessLogState>.FromData(state);
    }

    private async Task<TaskResult> CheckScopeAsync(AccessLogScope scope, long scopeId, long userId,
        AccessLogRecord record)
    {
        if (scope == AccessLogScope.Planet)
        {
            var planet = await _db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scopeId);
            if (planet is null)
                return TaskResult.FromFailure("Planet not found.");
            if (planet.LockedForMigration)
                return TaskResult.FromFailure(MigrationLock.Message);

            // Starting the log makes the planet private, and an open entry
            // makes it public; only the planet's owner does either. A public
            // planet's log still moves to a new owner with the planet.
            var isStart = record.Type is AccessLogEntryType.Genesis or AccessLogEntryType.Restart;
            if (!isStart && record.Type != AccessLogEntryType.TransferOwnership &&
                planet.EncryptionMode != PlanetEncryptionMode.InviteOnly)
                return TaskResult.FromFailure("This planet is not private.");
            if ((isStart || record.Type == AccessLogEntryType.Open) && planet.OwnerId != userId)
                return TaskResult.FromFailure("Only the planet owner can change whether the planet is private.");

            var isMember = await _db.PlanetMembers.AnyAsync(x => x.PlanetId == scopeId && x.UserId == userId && !x.IsDeleted);
            if (!isMember)
                return TaskResult.FromFailure("You are not a member of this planet.");

            return TaskResult.SuccessResult;
        }

        if (scope == AccessLogScope.GroupChannel)
        {
            var channel = await _db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scopeId);
            if (channel is null || channel.ChannelType != ChannelTypeEnum.GroupChat)
                return TaskResult.FromFailure("Group chat not found.");

            var membership = await _db.ChannelMembers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ChannelId == scopeId && x.UserId == userId);
            if (membership is null)
                return TaskResult.FromFailure("You are not in this group chat.");

            // Messages cannot be sent until a group has a log, so any member
            // may start one and becomes its owner in the log. Only the log's
            // owner may restart it.
            if (record.Type == AccessLogEntryType.Restart && !membership.IsAdmin)
                return TaskResult.FromFailure("Only the group's owner can restart its membership log.");

            return TaskResult.SuccessResult;
        }

        return TaskResult.FromFailure("Unknown access log.");
    }

    private async Task NotifyAsync(AccessLogScope scope, long scopeId)
    {
        var evt = new E2eeRealtimeEvent { Type = E2eeRealtimeEventTypes.AccessLogUpdated };
        if (scope == AccessLogScope.Planet)
        {
            evt.PlanetId = scopeId;
            await _hub.Clients.Group($"p-{scopeId}").SendAsync(E2eeRealtimeEvent.HubMethod, evt);
            return;
        }

        evt.ChannelId = scopeId;
        var members = await _db.ChannelMembers.AsNoTracking()
            .Where(x => x.ChannelId == scopeId)
            .Select(x => x.UserId)
            .ToListAsync();
        foreach (var member in members)
            await _realtime.SendToUserAsync(member, evt);
    }
}
