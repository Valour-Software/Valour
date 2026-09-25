using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;
using Valour.Sdk.E2ee;
using Valour.Shared;

namespace Valour.Server.Services;

/// <summary>
/// Stores and verifies users' key logs, user key boxes, and device-link
/// sessions. The server runs the same verification as clients so that invalid
/// data is refused early, but clients never rely on the server's checks.
///
/// Key logs belong to the hub, where users append to them. A community node
/// keeps a verified copy of the logs it needs, fetched from the hub when its
/// copy is older than <see cref="ReplicaFreshness"/>, so it can check message
/// signatures and membership logs for the planets it hosts.
/// </summary>
public class E2eeIdentityService
{
    public static readonly TimeSpan LinkSessionLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The largest sealed pin set an approving device may send to a new device.</summary>
    public const int MaxLinkPinsBytes = 2 * 1024 * 1024;

    /// <summary>How long a community node trusts its copy of a key log before asking the hub again.</summary>
    public static readonly TimeSpan ReplicaFreshness = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Entries an account's key log may hold. Every device change adds one,
    /// and every client that checks the account's messages downloads them.
    /// </summary>
    public const int MaxKeyLogEntries = 4096;

    private sealed record CachedState(int Count, UserKeyState State);

    /// <summary>Users each static cache keeps before it drops some (see <see cref="E2eeCacheLimit"/>).</summary>
    private const int MaxCachedUsers = 50_000;

    // Counts user key replacements (resets, device removals, and rotations)
    // this process has stored, whether appended here or copied from the hub.
    // Cached channel rotation checks include it, so any replacement makes
    // channels check again whether a removed device may hold their key.
    private static long _userKeyChanges;

    /// <summary>
    /// Changes whenever this process stores a key log entry that replaces a
    /// user key. Cached results that depend on who can open channel key boxes
    /// compare it to know when to recompute.
    /// </summary>
    public static long UserKeyChanges => Interlocked.Read(ref _userKeyChanges);

    private static bool ReplacesUserKey(UserKeyLogEntryType type) =>
        type is UserKeyLogEntryType.Reset or UserKeyLogEntryType.RevokeDevice or UserKeyLogEntryType.RotateUserKey;

    // Key logs are short, so a verified state per user is cheap to keep. A
    // count query detects entries appended through another node.
    private static readonly ConcurrentDictionary<long, CachedState> StateCache = new();

    // When a community node last refreshed each user's key log from the hub.
    private static readonly ConcurrentDictionary<long, DateTime> ReplicaSyncedAt = new();

    // After a failed request to the hub, a community node uses its own copies
    // until this time instead of waiting on the hub for every lookup.
    private static DateTime _hubRetryAt;
    private static int _hubFailures;
    private static readonly object HubBackoffLock = new();

    private readonly ValourDb _db;
    private readonly E2eeRealtimeService _realtime;
    private readonly FederationNodeClient _hub;
    private readonly ILogger<E2eeIdentityService> _logger;

    public E2eeIdentityService(ValourDb db, E2eeRealtimeService realtime, FederationNodeClient hub,
        ILogger<E2eeIdentityService> logger)
    {
        _db = db;
        _realtime = realtime;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>
    /// On a community node, brings the local copy of these users' key logs up
    /// to date with the hub. New entries are verified against the local copy
    /// before they are stored, so the hub cannot rewrite history the node
    /// already accepted. If the hub is unreachable, the local copy is used and
    /// the node waits before asking again, longer after each failure.
    /// </summary>
    private async Task SyncFromHubAsync(IEnumerable<long> userIds)
    {
        if (!FederationNodeService.NodeEnabled)
            return;

        var now = DateTime.UtcNow;
        lock (HubBackoffLock)
        {
            if (now < _hubRetryAt)
                return;
        }

        var stale = userIds.Distinct()
            .Where(id => !ReplicaSyncedAt.TryGetValue(id, out var at) || now - at > ReplicaFreshness)
            .ToList();

        foreach (var chunk in stale.Chunk(E2eeLimits.MaxUsersPerKeyLogRequest))
        {
            if (!await SyncChunkFromHubAsync(chunk, now))
                return;
        }
    }

    /// <summary>Fetches one request's worth of users. Returns false if the hub could not be reached.</summary>
    private async Task<bool> SyncChunkFromHubAsync(long[] stale, DateTime now)
    {
        var counts = await _db.E2eeKeyLogEntries.AsNoTracking()
            .Where(x => stale.Contains(x.UserId))
            .GroupBy(x => x.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        var result = await _hub.GetKeyLogsAsync(new UserKeyLogsRequest
        {
            KnownCounts = stale.ToDictionary(id => id, id => counts.GetValueOrDefault(id))
        });
        if (!result.Success || result.Data is null)
        {
            lock (HubBackoffLock)
            {
                _hubFailures = Math.Min(_hubFailures + 1, 10);
                var wait = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, _hubFailures - 1)));
                _hubRetryAt = DateTime.UtcNow + wait;
                _logger.LogWarning("Could not fetch key logs from the hub; using this node's copies for {Seconds} seconds",
                    (int)wait.TotalSeconds);
            }
            return false;
        }

        lock (HubBackoffLock)
            _hubFailures = 0;

        void MarkSynced(long userId)
        {
            E2eeCacheLimit.Trim(ReplicaSyncedAt, MaxCachedUsers);
            ReplicaSyncedAt[userId] = now;
        }

        foreach (var userId in stale)
        {
            if (!result.Data.TryGetValue(userId, out var fresh) || fresh is null || fresh.Count == 0)
            {
                MarkSynced(userId);
                continue;
            }

            var entries = (await GetLogAsync(userId)).Concat(fresh).ToList();
            UserKeyState verified;
            try
            {
                verified = UserKeyLogVerifier.Verify(userId, entries);
            }
            catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
            {
                // Asked again after the usual interval rather than on every lookup.
                _logger.LogWarning(e, "The hub's key log for user {UserId} does not extend this node's copy", userId);
                MarkSynced(userId);
                continue;
            }

            var firstFreshSeq = fresh.Min(x => x.Seq);
            var replacesKey = verified.Records.Any(r => r.Seq >= firstFreshSeq && ReplacesUserKey(r.Type));

            foreach (var entry in fresh)
            {
                _db.E2eeKeyLogEntries.Add(new Valour.Database.E2eeKeyLogEntry
                {
                    UserId = userId,
                    Seq = entry.Seq,
                    Body = entry.Body,
                    Signature = entry.Signature,
                    CreatedAt = now
                });
            }

            try
            {
                await _db.SaveChangesAsync();
                MarkSynced(userId);
                if (replacesKey)
                    Interlocked.Increment(ref _userKeyChanges);
            }
            catch (DbUpdateException)
            {
                // Another request stored the same entries first.
                _db.ChangeTracker.Clear();
            }
        }

        return true;
    }

    /// <summary>
    /// On a community node, the users among <paramref name="userIds"/> whose
    /// key logs this node has not fetched from the hub recently. Empty on the hub.
    /// </summary>
    public static List<long> NotSyncedFromHub(IEnumerable<long> userIds)
    {
        if (!FederationNodeService.NodeEnabled)
            return [];

        var now = DateTime.UtcNow;
        return userIds.Where(id => !ReplicaSyncedAt.TryGetValue(id, out var at) || now - at > ReplicaFreshness)
            .ToList();
    }

    // Key logs

    public async Task<List<UserKeyLogEntry>> GetLogAsync(long userId, int knownCount = 0)
    {
        var entries = await _db.E2eeKeyLogEntries
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.Seq >= knownCount)
            .OrderBy(x => x.Seq)
            .ToListAsync();

        return entries.Select(ToEntry).ToList();
    }

    public async Task<Dictionary<long, List<UserKeyLogEntry>>> GetLogsAsync(Dictionary<long, int> knownCounts)
    {
        var result = new Dictionary<long, List<UserKeyLogEntry>>();
        if (knownCounts is null || knownCounts.Count == 0)
            return result;

        var userIds = knownCounts.Keys.Take(E2eeLimits.MaxUsersPerKeyLogRequest).ToList();
        foreach (var userId in userIds)
            result[userId] = new List<UserKeyLogEntry>();

        // Only the entries each caller is missing are read. Users are grouped
        // by how many entries the caller has, which is usually a few values.
        foreach (var group in userIds.GroupBy(id => Math.Max(0, knownCounts[id])))
        {
            var ids = group.ToList();
            var known = group.Key;
            var rows = await _db.E2eeKeyLogEntries
                .AsNoTracking()
                .Where(x => ids.Contains(x.UserId) && x.Seq >= known)
                .OrderBy(x => x.UserId).ThenBy(x => x.Seq)
                .ToListAsync();

            foreach (var row in rows)
                result[row.UserId].Add(ToEntry(row));
        }

        return result;
    }

    /// <summary>
    /// Returns the verified key state for a user, or null if the user has not
    /// set up end-to-end encryption.
    /// </summary>
    public async Task<UserKeyState> GetStateAsync(long userId)
    {
        await SyncFromHubAsync([userId]);
        var count = await _db.E2eeKeyLogEntries.CountAsync(x => x.UserId == userId);
        return await GetVerifiedStateAsync(userId, count);
    }

    /// <summary>
    /// Returns the verified state for a user whose log has
    /// <paramref name="count"/> entries, from the cache when it is current.
    /// </summary>
    private async Task<UserKeyState> GetVerifiedStateAsync(long userId, int count)
    {
        if (count == 0)
            return null;

        if (StateCache.TryGetValue(userId, out var cached) && cached.Count == count)
            return cached.State;

        return VerifyAndCache(userId, await GetLogAsync(userId));
    }

    /// <summary>Verifies a user's whole stored log and caches the result.</summary>
    private UserKeyState VerifyAndCache(long userId, List<UserKeyLogEntry> entries)
    {
        UserKeyState state;
        try
        {
            state = UserKeyLogVerifier.Verify(userId, entries);
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            // Entries are verified before they are stored, so this means the
            // database was changed outside the API.
            _logger.LogError(e, "Stored key log for user {UserId} does not verify", userId);
            return null;
        }

        E2eeCacheLimit.Trim(StateCache, MaxCachedUsers);
        StateCache[userId] = new CachedState(entries.Count, state);
        return state;
    }

    public async Task<Dictionary<long, UserKeyState>> GetStatesAsync(IEnumerable<long> userIds)
    {
        var ids = userIds.Distinct().ToList();
        var result = new Dictionary<long, UserKeyState>();
        if (ids.Count == 0)
            return result;

        await SyncFromHubAsync(ids);

        // One count query for everyone; only users whose log changed are
        // verified again.
        var counts = new Dictionary<long, int>();
        foreach (var chunk in ids.Chunk(1000))
        {
            var chunkCounts = await _db.E2eeKeyLogEntries.AsNoTracking()
                .Where(x => chunk.Contains(x.UserId))
                .GroupBy(x => x.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var item in chunkCounts)
                counts[item.UserId] = item.Count;
        }

        var changed = new List<long>();
        foreach (var (userId, count) in counts)
        {
            if (StateCache.TryGetValue(userId, out var cached) && cached.Count == count)
                result[userId] = cached.State;
            else
                changed.Add(userId);
        }

        // The logs of users whose cached state is missing or out of date are
        // read together rather than one query per user.
        foreach (var chunk in changed.Chunk(1000))
        {
            var rows = await _db.E2eeKeyLogEntries.AsNoTracking()
                .Where(x => chunk.Contains(x.UserId))
                .OrderBy(x => x.UserId).ThenBy(x => x.Seq)
                .ToListAsync();

            foreach (var group in rows.GroupBy(x => x.UserId))
            {
                var state = VerifyAndCache(group.Key, group.Select(ToEntry).ToList());
                if (state is not null)
                    result[group.Key] = state;
            }
        }

        return result;
    }

    /// <summary>
    /// The users among <paramref name="userIds"/> who have set up encryption.
    /// On a community node, <paramref name="syncFromHub"/> first refreshes
    /// this node's copies of their key logs from the hub; without it, only
    /// the copies this node already has are checked.
    /// </summary>
    public async Task<HashSet<long>> GetUsersWithIdentityAsync(IReadOnlyCollection<long> userIds,
        bool syncFromHub = true)
    {
        if (userIds.Count == 0)
            return new HashSet<long>();

        if (syncFromHub)
            await SyncFromHubAsync(userIds);
        var ids = await _db.E2eeKeyLogEntries
            .AsNoTracking()
            .Where(x => x.Seq == 0 && userIds.Contains(x.UserId))
            .Select(x => x.UserId)
            .ToListAsync();
        return ids.ToHashSet();
    }

    /// <summary>
    /// Checks the membership log cutoffs of a device removal against the logs
    /// this server keeps. Entries a removed device signs after its cutoff have
    /// no effect, so a cutoff before an entry the device already signed would
    /// undo that entry for everyone, and one past the log's end would let the
    /// device's keys sign more entries. Logs kept by other nodes are checked
    /// by the removing device, which loads them first. The caller holds
    /// <see cref="LockUserKeysAsync"/>, so the device cannot sign another
    /// entry until the removal is stored.
    /// </summary>
    private async Task<TaskResult> CheckAccessLogCutoffsAsync(long userId, List<AccessLogCutoff> cutoffs,
        HashSet<string> removedDevices)
    {
        var scopeIds = cutoffs.Select(c => c.ScopeId).Distinct().ToList();
        var heads = (await _db.E2eeAccessLogEntries.AsNoTracking()
                .Where(x => scopeIds.Contains(x.ScopeId))
                .GroupBy(x => new { x.Scope, x.ScopeId })
                .Select(g => new { g.Key.Scope, g.Key.ScopeId, Head = g.Max(x => x.Seq) })
                .ToListAsync())
            .ToDictionary(x => ((AccessLogScope)x.Scope, x.ScopeId), x => x.Head);
        var signed = await _db.E2eeAccessLogEntries.AsNoTracking()
            .Where(x => scopeIds.Contains(x.ScopeId) && x.SignerUserId == userId)
            .Select(x => new { x.Scope, x.ScopeId, x.Seq, x.Body })
            .ToListAsync();

        foreach (var cutoff in cutoffs)
        {
            if (!heads.TryGetValue((cutoff.Scope, cutoff.ScopeId), out var head))
                continue;
            if (cutoff.Seq > head)
                return TaskResult.FromFailure($"{E2eeErrorCodes.RemovalCutoffStale}: A membership log changed " +
                                              "while this device was being removed. Try again.");

            foreach (var entry in signed.Where(x => (AccessLogScope)x.Scope == cutoff.Scope &&
                                                    x.ScopeId == cutoff.ScopeId && x.Seq > cutoff.Seq))
            {
                string signerDeviceId;
                try
                {
                    signerDeviceId = AccessLogRecord.Decode(entry.Body).SignerDeviceId;
                }
                catch (E2eeFormatException)
                {
                    continue;
                }

                if (removedDevices.Contains(signerDeviceId))
                    return TaskResult.FromFailure($"{E2eeErrorCodes.RemovalCutoffStale}: The removed device " +
                                                  "changed a membership log this device has not loaded yet. Try again.");
            }
        }

        return TaskResult.SuccessResult;
    }

    private const int UserKeysLockClass = 0x564B4C4B; // "VKLK"

    /// <summary>
    /// Serializes changes to a user's key log with the membership log entries
    /// their devices sign, so a device being removed cannot sign an entry
    /// between the removal's checks and its storage. Returns a transaction to
    /// commit, or null when the lock joined the caller's transaction, which
    /// then holds it until it ends.
    /// </summary>
    public async Task<IDbContextTransaction> LockUserKeysAsync(long userId)
    {
        var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync()
            : null;
        var key = (int)(userId ^ (userId >> 32));
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({UserKeysLockClass}, {key})");
        return transaction;
    }

    /// <summary>
    /// Verifies and appends an entry to the caller's key log, along with any
    /// user key boxes that go with it.
    /// </summary>
    public async Task<TaskResult<UserKeyState>> AppendAsync(long userId, UserKeyLogEntry entry,
        List<UserKeyBoxDto> boxes, bool allowAddDevice = true)
    {
        if (entry?.Body is null || entry.Signature is null)
            return TaskResult<UserKeyState>.FromFailure("Include the key log entry.");
        if (FederationNodeService.NodeEnabled)
            return TaskResult<UserKeyState>.FromFailure("Key logs are kept by the Valour hub. Change your keys there.");
        if (entry.UserId != userId)
            return TaskResult<UserKeyState>.FromFailure("You can only change your own key log.");
        if (entry.Body.Length > 16 * 1024)
            return TaskResult<UserKeyState>.FromFailure("Key log entry is too large.");

        await using var keysLock = await LockUserKeysAsync(userId);
        var existing = await GetLogAsync(userId);
        if (existing.Count >= MaxKeyLogEntries)
            return TaskResult<UserKeyState>.FromFailure("This account's key history is full. Contact Valour support.");

        UserKeyState state;
        UserKeyLogRecord record;
        HashSet<string> removedDevices;
        try
        {
            state = UserKeyLogVerifier.Verify(userId, existing);
            var activeBefore = state.ActiveDevices.Keys.ToHashSet();
            record = UserKeyLogVerifier.Apply(state, entry);
            removedDevices = activeBefore.Where(id => !state.ActiveDevices.ContainsKey(id)).ToHashSet();
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return TaskResult<UserKeyState>.FromFailure(e.Message);
        }

        if (record.AccessLogCutoffs is { Count: > 0 })
        {
            var cutoffCheck = await CheckAccessLogCutoffsAsync(userId, record.AccessLogCutoffs, removedDevices);
            if (!cutoffCheck.Success)
                return TaskResult<UserKeyState>.FromFailure(cutoffCheck.Message);
        }

        // Devices are valid from and until the times in the log, which other
        // devices compare with when messages were sent, so a device with a
        // badly wrong clock cannot change keys.
        var skew = TimeSpan.FromMinutes(10);
        var serverTime = DateTimeOffset.UtcNow;
        if (record.TimestampMs > serverTime.Add(skew).ToUnixTimeMilliseconds() ||
            record.TimestampMs < serverTime.Subtract(skew).ToUnixTimeMilliseconds())
            return TaskResult<UserKeyState>.FromFailure("This device's clock is wrong. Correct it and try again.");

        if (!allowAddDevice && record.Type == UserKeyLogEntryType.AddDevice &&
            !record.SignerId.StartsWith(RecoveryKey.IdPrefix, StringComparison.Ordinal))
            return TaskResult<UserKeyState>.FromFailure("Add devices by approving a link request.");

        var boxResult = ValidateBoxes(state, boxes ?? []);
        if (!boxResult.Success)
            return TaskResult<UserKeyState>.FromFailure(boxResult.Message);

        // Every device that can act for the user must be able to open the
        // current user key after the change, or it would lose access.
        if (record.Type is UserKeyLogEntryType.Genesis or UserKeyLogEntryType.Reset
                or UserKeyLogEntryType.RevokeDevice or UserKeyLogEntryType.RotateUserKey)
        {
            var provided = (boxes ?? []).Select(b => b.RecipientId).ToHashSet();
            var required = state.ActiveDevices.Keys.ToList();
            if (state.Recovery is not null)
                required.Add(state.Recovery.DeviceId);
            if (required.Any(id => !provided.Contains(id)))
                return TaskResult<UserKeyState>.FromFailure("Include a user key box for every remaining device.");
        }

        // A new recovery key is useless unless it can open the current user key.
        if (record.Type == UserKeyLogEntryType.SetRecovery && state.Recovery is not null &&
            (boxes ?? []).All(b => b.RecipientId != state.Recovery.DeviceId))
            return TaskResult<UserKeyState>.FromFailure("Include a user key box for the new recovery key.");

        var now = DateTime.UtcNow;
        _db.E2eeKeyLogEntries.Add(new Valour.Database.E2eeKeyLogEntry
        {
            UserId = userId,
            Seq = entry.Seq,
            Body = entry.Body,
            Signature = entry.Signature,
            CreatedAt = now
        });

        await UpsertBoxesAsync(userId, boxes ?? [], now);

        try
        {
            await _db.SaveChangesAsync();
            if (keysLock is not null)
                await keysLock.CommitAsync();
        }
        catch (DbUpdateException)
        {
            // Another device appended at the same sequence number first.
            _db.ChangeTracker.Clear();
            return TaskResult<UserKeyState>.FromFailure("Your keys changed on another device. Try again.");
        }

        StateCache[userId] = new CachedState(state.HeadSeq + 1, state);

        // A replaced user key means a removed device may still open boxes
        // sealed to the old one, so channels re-check whether they need a new key.
        if (ReplacesUserKey(record.Type))
            Interlocked.Increment(ref _userKeyChanges);

        await _realtime.SendToUserAsync(userId, new E2eeRealtimeEvent
        {
            Type = E2eeRealtimeEventTypes.KeyLogUpdated,
            UserId = userId
        });

        return TaskResult<UserKeyState>.FromData(state);
    }

    // User key boxes

    public async Task<UserKeyBoxDto> GetBoxAsync(long userId, string recipientId, int? generation)
    {
        var query = _db.E2eeUserKeyBoxes.AsNoTracking()
            .Where(x => x.UserId == userId && x.RecipientId == recipientId);
        if (generation is not null)
            query = query.Where(x => x.Generation == generation);

        var box = await query.OrderByDescending(x => x.Generation).FirstOrDefaultAsync();
        return box is null
            ? null
            : new UserKeyBoxDto { UserId = box.UserId, Generation = box.Generation, RecipientId = box.RecipientId, Box = box.Box };
    }

    public async Task<TaskResult> StoreBoxesAsync(long userId, List<UserKeyBoxDto> boxes)
    {
        var state = await GetStateAsync(userId);
        if (state is null)
            return TaskResult.FromFailure("Set up encryption first.");

        var result = ValidateBoxes(state, boxes ?? []);
        if (!result.Success)
            return result;

        // Two devices can store the same box at the same moment; the second
        // attempt updates the row the first one inserted.
        for (var attempt = 0; ; attempt++)
        {
            await UpsertBoxesAsync(userId, boxes ?? [], DateTime.UtcNow);
            try
            {
                await _db.SaveChangesAsync();
                return TaskResult.SuccessResult;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                _db.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                return TaskResult.FromFailure("Your keys changed on another device. Try again.");
            }
        }
    }

    private static TaskResult ValidateBoxes(UserKeyState state, List<UserKeyBoxDto> boxes)
    {
        if (boxes.Count > 64)
            return TaskResult.FromFailure("Too many key boxes.");

        foreach (var box in boxes)
        {
            if (box.UserId != state.UserId)
                return TaskResult.FromFailure("Key box belongs to another user.");
            if (box.Generation != state.UserKey.Generation)
                return TaskResult.FromFailure("Key boxes must be for the current user key.");
            if (box.Box is null || box.Box.Length > 1024)
                return TaskResult.FromFailure("Invalid key box.");

            var isDevice = state.ActiveDevices.ContainsKey(box.RecipientId ?? string.Empty);
            var isRecovery = state.Recovery is not null && state.Recovery.DeviceId == box.RecipientId;
            if (!isDevice && !isRecovery)
                return TaskResult.FromFailure("Key boxes can only be sealed to active devices or the recovery key.");
        }

        return TaskResult.SuccessResult;
    }

    private async Task UpsertBoxesAsync(long userId, List<UserKeyBoxDto> boxes, DateTime now)
    {
        foreach (var box in boxes)
        {
            var existing = await _db.E2eeUserKeyBoxes.FindAsync(userId, box.Generation, box.RecipientId);
            if (existing is not null)
            {
                existing.Box = box.Box;
                existing.CreatedAt = now;
            }
            else
            {
                _db.E2eeUserKeyBoxes.Add(new Valour.Database.E2eeUserKeyBox
                {
                    UserId = userId,
                    Generation = box.Generation,
                    RecipientId = box.RecipientId,
                    Box = box.Box,
                    CreatedAt = now
                });
            }
        }
    }

    // Device linking

    public async Task<TaskResult<DeviceLinkSessionDto>> CreateLinkSessionAsync(long userId, CreateDeviceLinkRequest request)
    {
        var state = await GetStateAsync(userId);
        if (state is null)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("Set up encryption on this account first.");

        var active = await _db.E2eeDeviceLinkSessions.CountAsync(x =>
            x.UserId == userId && x.ExpiresAt > DateTime.UtcNow &&
            x.Status < (int)DeviceLinkStatus.Approved);
        if (active >= 5)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("Too many pending device links. Wait a few minutes and try again.");

        // A code an existing device shows names its session by an ID derived
        // from the code's secret, so a new device that types the code can
        // find it.
        var sessionId = Base64Url.Encode(E2eeCrypto.RandomBytes(16));
        if (request.Mode == DeviceLinkMode.ExistingDeviceShowsCode && request.SessionId is not null)
        {
            if (!IsValidSessionId(request.SessionId))
                return TaskResult<DeviceLinkSessionDto>.FromFailure("Invalid link session ID.");
            if (await _db.E2eeDeviceLinkSessions.AnyAsync(x => x.Id == request.SessionId))
                return TaskResult<DeviceLinkSessionDto>.FromFailure("That link code is already in use. Show a new code.");
            sessionId = request.SessionId;
        }

        var now = DateTime.UtcNow;
        var session = new Valour.Database.E2eeDeviceLinkSession
        {
            Id = sessionId,
            UserId = userId,
            Mode = (int)request.Mode,
            CreatedAt = now,
            ExpiresAt = now + LinkSessionLifetime
        };

        if (request.Mode == DeviceLinkMode.NewDeviceShowsCode)
        {
            var deviceResult = ValidateNewDevice(state, request.Device);
            if (!deviceResult.Success)
                return TaskResult<DeviceLinkSessionDto>.FromFailure(deviceResult.Message);

            ApplyDevice(session, request.Device);
            session.Status = (int)DeviceLinkStatus.WaitingForApproval;
        }
        else
        {
            session.Status = (int)DeviceLinkStatus.WaitingForNewDevice;
        }

        _db.E2eeDeviceLinkSessions.Add(session);
        await _db.SaveChangesAsync();

        var dto = ToDto(session);
        await NotifySessionAsync(dto);
        return TaskResult<DeviceLinkSessionDto>.FromData(dto);
    }

    public async Task<DeviceLinkSessionDto> GetLinkSessionAsync(long userId, string sessionId)
    {
        var session = await _db.E2eeDeviceLinkSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId);
        return session is null ? null : ToDto(session, includePins: true);
    }

    public async Task<List<DeviceLinkSessionDto>> GetPendingLinkSessionsAsync(long userId)
    {
        var now = DateTime.UtcNow;
        var sessions = await _db.E2eeDeviceLinkSessions.AsNoTracking()
            .Where(x => x.UserId == userId && x.ExpiresAt > now && x.Status == (int)DeviceLinkStatus.WaitingForApproval)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
        return sessions.Select(x => ToDto(x)).ToList();
    }

    public async Task<TaskResult<DeviceLinkSessionDto>> JoinLinkSessionAsync(long userId, string sessionId,
        JoinDeviceLinkRequest request)
    {
        var session = await _db.E2eeDeviceLinkSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId);
        if (session is null || session.ExpiresAt <= DateTime.UtcNow)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("This link code has expired. Show a new code on your other device.");
        if (session.Mode != (int)DeviceLinkMode.ExistingDeviceShowsCode ||
            session.Status != (int)DeviceLinkStatus.WaitingForNewDevice)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("This link code was already used.");
        if (request.JoinMac?.Length != 32)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("Invalid link proof.");

        var state = await GetStateAsync(userId);
        var deviceResult = ValidateNewDevice(state, request.Device);
        if (!deviceResult.Success)
            return TaskResult<DeviceLinkSessionDto>.FromFailure(deviceResult.Message);

        ApplyDevice(session, request.Device);
        session.JoinMac = request.JoinMac;
        session.Status = (int)DeviceLinkStatus.WaitingForApproval;
        await _db.SaveChangesAsync();

        var dto = ToDto(session);
        await NotifySessionAsync(dto);
        return TaskResult<DeviceLinkSessionDto>.FromData(dto);
    }

    public async Task<TaskResult<DeviceLinkSessionDto>> ApproveLinkSessionAsync(long userId, string sessionId,
        ApproveDeviceLinkRequest request)
    {
        var session = await _db.E2eeDeviceLinkSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId);
        if (session is null || session.ExpiresAt <= DateTime.UtcNow)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("This link request has expired.");
        if (session.Status != (int)DeviceLinkStatus.WaitingForApproval)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("This link request is not waiting for approval.");
        if (request?.Entry is null || request.Box is null)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("Include the signed entry and key box.");

        // The new device checks this proof with the link secret, which the
        // server never sees; the server only relays it.
        if (request.ApprovalMac?.Length != E2eeCrypto.HashSize)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("Include the approval proof.");
        if (request.Pins?.Length > MaxLinkPinsBytes)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("Too much key data to send to the new device.");

        // The entry must add exactly the device in this session.
        UserKeyLogRecord record;
        try
        {
            record = UserKeyLogRecord.Decode(request.Entry.Body);
        }
        catch (E2eeFormatException e)
        {
            return TaskResult<DeviceLinkSessionDto>.FromFailure(e.Message);
        }

        if (record.Type != UserKeyLogEntryType.AddDevice || record.Device.Keys.DeviceId != session.DeviceId ||
            !record.Device.Keys.SignPublicKey.SequenceEqual(session.DeviceSignPublicKey) ||
            !record.Device.Keys.EncryptPublicKey.SequenceEqual(session.DeviceEncryptPublicKey))
            return TaskResult<DeviceLinkSessionDto>.FromFailure("The approval does not match this device.");
        if (request.Box.RecipientId != session.DeviceId)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("The key box is not for this device.");

        var result = await AppendAsync(userId, request.Entry, [request.Box]);
        if (!result.Success)
            return TaskResult<DeviceLinkSessionDto>.FromFailure(result.Message);

        session.Status = (int)DeviceLinkStatus.Approved;
        session.ApprovedByDeviceId = record.SignerId;
        session.ApprovalMac = request.ApprovalMac;
        session.ApprovalPins = request.Pins;
        await _db.SaveChangesAsync();

        var dto = ToDto(session);
        await NotifySessionAsync(dto);
        return TaskResult<DeviceLinkSessionDto>.FromData(dto);
    }

    public async Task<TaskResult> DenyLinkSessionAsync(long userId, string sessionId)
    {
        var session = await _db.E2eeDeviceLinkSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId);
        if (session is null)
            return TaskResult.FromFailure("Link request not found.");
        if (session.Status == (int)DeviceLinkStatus.Approved)
            return TaskResult.FromFailure("This device was already approved. Remove it from your devices instead.");

        session.Status = (int)DeviceLinkStatus.Denied;
        await _db.SaveChangesAsync();
        await NotifySessionAsync(ToDto(session));
        return TaskResult.SuccessResult;
    }

    public async Task<int> DeleteExpiredLinkSessionsAsync()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);
        return await _db.E2eeDeviceLinkSessions.Where(x => x.ExpiresAt < cutoff).ExecuteDeleteAsync();
    }

    private static TaskResult ValidateNewDevice(UserKeyState state, DeviceDescriptorDto device)
    {
        if (device is null)
            return TaskResult.FromFailure("Include the new device's keys.");

        try
        {
            var descriptor = device.ToDescriptor();
            descriptor.Keys.Validate();
            if (!DeviceJoinProof.Verify(state.UserId, descriptor.Keys, descriptor.JoinProof))
                return TaskResult.FromFailure("The device did not prove it belongs to this account.");
            if (state.EverDevices.ContainsKey(descriptor.Keys.DeviceId))
                return TaskResult.FromFailure("This device is already registered.");
            if ((descriptor.Name?.Length ?? 0) > 64)
                return TaskResult.FromFailure("Device name is too long.");
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return TaskResult.FromFailure(e.Message);
        }

        return TaskResult.SuccessResult;
    }

    private static bool IsValidSessionId(string id)
    {
        if (id.Length != 22)
            return false;
        try
        {
            return Base64Url.Decode(id).Length == 16;
        }
        catch (Exception e) when (e is E2eeFormatException or FormatException)
        {
            return false;
        }
    }

    private static void ApplyDevice(Valour.Database.E2eeDeviceLinkSession session, DeviceDescriptorDto device)
    {
        session.DeviceId = device.DeviceId;
        session.DeviceSignPublicKey = device.SignPublicKey;
        session.DeviceEncryptPublicKey = device.EncryptPublicKey;
        session.DeviceName = device.Name;
        session.DeviceJoinProof = device.JoinProof;
    }

    private Task NotifySessionAsync(DeviceLinkSessionDto session) =>
        _realtime.SendToUserAsync(session.UserId, new E2eeRealtimeEvent
        {
            Type = E2eeRealtimeEventTypes.LinkSession,
            UserId = session.UserId,
            LinkSession = session
        });

    /// <summary>
    /// Pins can be large, so they are only included when the new device
    /// fetches the session, not in realtime events or lists.
    /// </summary>
    private static DeviceLinkSessionDto ToDto(Valour.Database.E2eeDeviceLinkSession session, bool includePins = false)
    {
        var status = (DeviceLinkStatus)session.Status;
        if (session.ExpiresAt <= DateTime.UtcNow && status < DeviceLinkStatus.Approved)
            status = DeviceLinkStatus.Expired;

        return new DeviceLinkSessionDto
        {
            Id = session.Id,
            UserId = session.UserId,
            Mode = (DeviceLinkMode)session.Mode,
            Status = status,
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt,
            JoinMac = session.JoinMac,
            ApprovedByDeviceId = session.ApprovedByDeviceId,
            ApprovalMac = session.ApprovalMac,
            ApprovalPins = includePins ? session.ApprovalPins : null,
            Device = session.DeviceId is null
                ? null
                : new DeviceDescriptorDto
                {
                    DeviceId = session.DeviceId,
                    SignPublicKey = session.DeviceSignPublicKey,
                    EncryptPublicKey = session.DeviceEncryptPublicKey,
                    Name = session.DeviceName,
                    JoinProof = session.DeviceJoinProof
                }
        };
    }

    private static UserKeyLogEntry ToEntry(Valour.Database.E2eeKeyLogEntry row) => new()
    {
        UserId = row.UserId,
        Seq = row.Seq,
        Body = row.Body,
        Signature = row.Signature
    };
}
