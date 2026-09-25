using System.Collections.Concurrent;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

/// <summary>
/// The channel keys this device holds, with each generation's verified record.
/// </summary>
public sealed class ChannelKeyRing
{
    public long ChannelId { get; init; }
    public long? PlanetId { get; init; }

    /// <summary>
    /// Who may hold the channel's keys. A device never relaxes this below what
    /// a verified membership log or a signed key record established.
    /// </summary>
    public ChannelKeyPolicy Policy { get; internal set; }

    /// <summary>
    /// Whether new members may read earlier messages, as signed into the
    /// newest trusted key record, or as the server reports it when there is none.
    /// </summary>
    public bool SharesHistory { get; internal set; }

    /// <summary>Whether the server reports that new members may read history.</summary>
    public bool ReportedSharesHistory { get; init; }

    public int LatestGeneration { get; set; }
    public bool RotationRequired { get; set; }
    public DateTime FetchedAt { get; init; }

    /// <summary>
    /// True when the server reported an older newest key than this device
    /// already saw. The device does not send until the server catches up.
    /// </summary>
    public bool RolledBack { get; init; }

    /// <summary>
    /// Verified generation records. They are loaded for the recent generations
    /// and the ones this device holds, and for older generations when older
    /// messages are read.
    /// </summary>
    public ConcurrentDictionary<int, ChannelKeyGenerationRecord> Records { get; } = new();

    public ConcurrentDictionary<int, ChannelKeySecret> Secrets { get; } = new();

    /// <summary>
    /// Generations whose creator this device confirmed may make the channel's
    /// keys under its policy, with key epochs admitted by the membership log
    /// or accepted by the person. Other records are still used to read, since
    /// every message is signed by its author, but never to send.
    /// </summary>
    public ConcurrentDictionary<int, byte> Trusted { get; } = new();

    /// <summary>The channel's newest search key generations, newest first.</summary>
    public List<int> IndexGenerations { get; init; } = new();

    public ChannelKeySecret Latest => Secrets.GetValueOrDefault(LatestGeneration);

    public ChannelKeyGenerationRecord LatestRecord => Records.GetValueOrDefault(LatestGeneration);

    public bool IsGoverned => Policy is ChannelKeyPolicy.Group or ChannelKeyPolicy.PlanetInviteOnly;
}

public partial class E2eeService
{
    private static readonly TimeSpan KeyRingLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan KeyRequestInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a member without a channel's key waits for someone online to
    /// share it before starting a new key so they can send.
    /// </summary>
    private static readonly TimeSpan KeyShareWait = TimeSpan.FromSeconds(5);

    // Channel IDs are only unique within one node, but planet IDs are global
    // and direct chats live on the hub, so a channel is keyed by both.
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), ChannelKeyRing> _keyRings = new();
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), SemaphoreSlim> _ringLocks = new();
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), DateTime> _lastKeyRequests = new();
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), byte> _scheduledServes = new();

    private static (long PlanetId, long ChannelId) ChannelKey(Channel channel) => (channel.PlanetId ?? 0, channel.Id);
    private static (long PlanetId, long ChannelId) ChannelKey(long channelId, long? planetId) => (planetId ?? 0, channelId);

    /// <summary>
    /// Returns the keys this device holds for a channel, fetching and
    /// verifying them when needed.
    /// </summary>
    public async Task<ChannelKeyRing> GetKeyRingAsync(Channel channel, bool refresh = false)
    {
        if (!refresh && _keyRings.TryGetValue(ChannelKey(channel), out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < KeyRingLifetime &&
            cached.LatestGeneration >= channel.EncryptionGeneration)
            return cached;

        var gate = _ringLocks.GetOrAdd(ChannelKey(channel), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!refresh && _keyRings.TryGetValue(ChannelKey(channel), out cached) &&
                DateTime.UtcNow - cached.FetchedAt < TimeSpan.FromSeconds(2))
                return cached;

            // A fetch that failed moments ago is not repeated for every
            // message on screen.
            if (_keyRingFailures.TryGetValue(ChannelKey(channel), out var failure) && failure.IsWaiting)
                return _keyRings.GetValueOrDefault(ChannelKey(channel));

            var result = await channel.Node.GetJsonAsync<ChannelKeyStateDto>(ChannelRoute(channel, "keys"),
                cacheDurationMs: null);
            if (!result.Success || result.Data is null)
            {
                _keyRingFailures[ChannelKey(channel)] = (failure ?? new FailureBackoff()).Next();
                return _keyRings.GetValueOrDefault(ChannelKey(channel));
            }

            _keyRingFailures.TryRemove(ChannelKey(channel), out _);
            var ring = await BuildKeyRingAsync(channel, result.Data, _keyRings.GetValueOrDefault(ChannelKey(channel)));
            _keyRings[ChannelKey(channel)] = ring;
            return ring;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ChannelKeyRing> BuildKeyRingAsync(Channel channel, ChannelKeyStateDto state, ChannelKeyRing previous)
    {
        var ring = new ChannelKeyRing
        {
            ChannelId = channel.Id,
            PlanetId = channel.PlanetId,
            Policy = await EffectivePolicyAsync(channel, state.Policy),
            SharesHistory = state.SharesHistory,
            ReportedSharesHistory = state.SharesHistory,
            LatestGeneration = state.LatestGeneration,
            RotationRequired = state.RotationRequired,
            RolledBack = state.LatestGeneration < await GetGenerationPinAsync(channel),
            IndexGenerations = state.IndexGenerations ?? [],
            FetchedAt = DateTime.UtcNow
        };

        // Records never change once published, so ones this device already
        // verified, including older ones loaded for older messages, are kept.
        if (previous is not null)
        {
            foreach (var (generation, record) in previous.Records)
                ring.Records[generation] = record;
        }

        await AddRecordsAsync(channel, ring, state.Generations.Where(e => !ring.Records.ContainsKey(e.Generation)));

        // Trust depends on the membership log and accepted key resets, which
        // may have changed since the kept records were checked.
        await RecheckTrustAsync(channel, ring);
        await ApplySignedTermsAsync(channel, ring);
        if (ring.LatestRecord is not null)
            await PinGenerationAsync(channel, ring.LatestGeneration);

        // Keep secrets this device already opened, then add newly boxed ones.
        if (previous is not null)
        {
            foreach (var (generation, secret) in previous.Secrets)
            {
                if (ring.Records.TryGetValue(generation, out var record) && secret.Matches(record))
                    ring.Secrets[generation] = secret;
            }
        }

        foreach (var box in state.MyBoxes)
        {
            if (ring.Secrets.ContainsKey(box.Generation) || !ring.Records.TryGetValue(box.Generation, out var record))
                continue;

            var userKey = await GetUserKeyAsync(box.UserKeyGeneration);
            if (userKey is null)
                continue;

            ChannelKeySecret secret = null;
            try
            {
                secret = ChannelKeySecret.OpenBox(channel.Id, box.Generation, _client.Me.Id, userKey, box.Box);
            }
            catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
            {
            }

            if (secret is not null && secret.Matches(record))
            {
                ring.Secrets[box.Generation] = secret;
                continue;
            }

            // A box that does not open is removed so another member can share
            // a working one.
            LogWarning($"Could not open key box for generation {box.Generation} in channel {channel.Id}");
            var generation = box.Generation;
            _ = RunInBackgroundAsync(() => channel.Node.DeleteAsync(ChannelRouteWith(channel, $"boxes/{generation}")),
                $"Removing an unreadable key box in channel {channel.Id} failed");
        }

        UnlockEarlierSecrets(channel, ring);
        PruneOwnBoxes(channel, ring, state.MyBoxes.Select(b => b.Generation).ToHashSet());
        return ring;
    }

    /// <summary>
    /// Asks the server to drop this user's boxes for generations that a later
    /// box of theirs unlocks. Only the part of the chain this device opened
    /// itself is pruned, since the server cannot check the chain.
    /// </summary>
    private void PruneOwnBoxes(Channel channel, ChannelKeyRing ring, HashSet<int> boxed)
    {
        var top = boxed.Where(ring.Secrets.ContainsKey).DefaultIfEmpty(0).Max();
        if (top == 0)
            return;

        var secret = ring.Secrets[top];
        var floor = top;
        while (floor > 1 && ring.Records.TryGetValue(floor, out var record) && record.UnlocksPrevious &&
               ring.Records.TryGetValue(floor - 1, out var previous))
        {
            try
            {
                var older = secret.UnwrapPrevious(record.PreviousWrapped);
                if (!older.Matches(previous))
                    break;
                secret = older;
                floor--;
            }
            catch (E2eeVerificationException)
            {
                break;
            }
        }

        if (!boxed.Any(g => g >= floor && g < top))
            return;

        _ = RunInBackgroundAsync(() => channel.Node.PostAsync(
                ChannelRouteWith(channel, "boxes/prune") + $"through={top}&floor={floor}", (object)null),
            $"Removing superseded key boxes in channel {channel.Id} failed");
    }

    /// <summary>
    /// Verifies generation records and adds the valid ones to the ring.
    /// </summary>
    private async Task AddRecordsAsync(Channel channel, ChannelKeyRing ring, IEnumerable<ChannelKeyGenerationEntry> entries)
    {
        var list = entries.Select(e => (Entry: e, CreatorId: SafeDecodeCreator(e.Body))).ToList();
        if (list.Count == 0)
            return;

        var creators = list
            .Select(x => x.CreatorId)
            .Where(id => id is not null && id != ChannelKeyGenerationRecord.ServerCreatorId)
            .Select(id => id!.Value);
        var creatorStates = await GetUserStatesAsync(creators);

        foreach (var (entry, creatorId) in list)
        {
            try
            {
                // The server may create a channel's first key to seal messages
                // it wrote. It is signed with the server's attestation key.
                if (ChannelKeyGenerationRecord.TryReadIsServerCreated(entry.Body))
                {
                    var serverRecord = ChannelKeyGenerationRecord.Decode(entry.Body);
                    var keyId = serverRecord.CreatorDeviceId[ChannelKeyGenerationRecord.ServerDevicePrefix.Length..];
                    var serverKey = await ResolveServerKeyAsync(channel.Node, keyId);
                    var verifiedServerRecord = ChannelKeyGenerationRecord.VerifyServer(entry, _ => serverKey);
                    if (verifiedServerRecord.PlanetId != (channel.PlanetId ?? 0))
                        throw new E2eeVerificationException("Channel key generation belongs to another planet.");
                    ring.Records[entry.Generation] = verifiedServerRecord;
                    continue;
                }

                if (creatorId is null || !creatorStates.TryGetValue(creatorId.Value, out var creatorState))
                    continue;

                // A record dated outside its device's time on the account, for
                // example just after the device was removed, still unlocks
                // history but is never trusted to send with.
                ChannelKeyGenerationRecord record;
                var datedValidly = true;
                try
                {
                    record = ChannelKeyGenerationRecord.Verify(entry, creatorState);
                }
                catch (E2eeVerificationException)
                {
                    record = ChannelKeyGenerationRecord.VerifyForReading(entry, creatorState);
                    datedValidly = false;
                }

                if (record.PlanetId != (channel.PlanetId ?? 0))
                    throw new E2eeVerificationException("Channel key generation belongs to another planet.");

                ring.Records[record.Generation] = record;
                if (datedValidly && await IsTrustedCreatorAsync(channel, ring, record, creatorState))
                    ring.Trusted[record.Generation] = 0;
                else
                    LogWarning($"Key generation {record.Generation} in channel {channel.Id} was made by keys this device does not accept, so it is only used to read.");
            }
            catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
            {
                LogWarning($"Ignoring invalid key generation {entry.Generation} in channel {channel.Id}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Each generation that unlocks history carries the one before it, so a
    /// held secret unlocks every earlier one down to the previous break.
    /// </summary>
    private void UnlockEarlierSecrets(Channel channel, ChannelKeyRing ring)
    {
        if (ring.Secrets.Count == 0)
            return;

        for (var generation = ring.Secrets.Keys.Max(); generation > 1; generation--)
        {
            if (!ring.Secrets.TryGetValue(generation, out var secret) || ring.Secrets.ContainsKey(generation - 1))
                continue;
            if (!ring.Records.TryGetValue(generation, out var record) || !record.UnlocksPrevious ||
                !ring.Records.TryGetValue(generation - 1, out var previousRecord))
                continue;

            try
            {
                var older = secret.UnwrapPrevious(record.PreviousWrapped);
                if (older.Matches(previousRecord))
                    ring.Secrets[generation - 1] = older;
            }
            catch (E2eeVerificationException)
            {
                LogWarning($"Could not unlock generation {generation - 1} in channel {channel.Id}");
            }
        }
    }

    /// <summary>
    /// Loads and verifies the records this device has not loaded in a range
    /// of generations, then unlocks what it can with them.
    /// </summary>
    private async Task EnsureRecordsAsync(Channel channel, ChannelKeyRing ring, int from, int to)
    {
        var gate = _ringLocks.GetOrAdd(ChannelKey(channel), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var missing = Enumerable.Range(from, Math.Max(0, to - from + 1))
                .Where(g => !ring.Records.ContainsKey(g))
                .ToList();

            while (missing.Count > 0)
            {
                var first = missing[0];
                var last = Math.Min(missing[^1], first + E2eeLimits.MaxBoxesPerRequest - 1);
                var result = await channel.Node.GetJsonAsync<List<ChannelKeyGenerationEntry>>(
                    ChannelRouteWith(channel, "generations") + $"from={first}&to={last}", cacheDurationMs: null);
                if (!result.Success || result.Data is not { Count: > 0 })
                    break;

                await AddRecordsAsync(channel, ring, result.Data.Where(e => !ring.Records.ContainsKey(e.Generation)));
                missing = missing.Where(g => g > last).ToList();
            }

            UnlockEarlierSecrets(channel, ring);
        }
        finally
        {
            gate.Release();
        }
    }

    private static long? SafeDecodeCreator(byte[] body)
    {
        try
        {
            return ChannelKeyGenerationRecord.Decode(body).CreatorUserId;
        }
        catch (E2eeFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Applies the rules signed into the key records. A record signed under an
    /// invite-only planet's membership log keeps the channel governed by that
    /// log even on a device that never pinned it. In governed channels the
    /// newest trusted record also decides whether new members may read
    /// history, since the server's word is not trusted there.
    /// </summary>
    private async Task ApplySignedTermsAsync(Channel channel, ChannelKeyRing ring)
    {
        var governed = channel.PlanetId is null ? ChannelKeyPolicy.Group : ChannelKeyPolicy.PlanetInviteOnly;
        if (!ring.IsGoverned && ring.Records.Values.Any(r => !r.IsServerCreated && r.Terms.Policy == governed))
        {
            LogWarning($"Channel {channel.Id} was reported as {ring.Policy}, but its keys were made under a membership log");
            ring.Policy = governed;
            await RecheckTrustAsync(channel, ring);
        }

        // The first trusted key sets the history rule; later keys may only
        // change it if the log's owner made them.
        if (!ring.IsGoverned || ring.Trusted.IsEmpty)
            return;

        var log = await GovernedLogAsync(channel, ring.Policy);
        bool? sharesHistory = null;
        foreach (var generation in ring.Trusted.Keys.Order())
        {
            if (!ring.Records.TryGetValue(generation, out var record))
                continue;
            if (sharesHistory is null || record.CreatorUserId == log?.Owner.UserId)
                sharesHistory = record.Terms.SharesHistory;
        }

        if (sharesHistory is not null)
            ring.SharesHistory = sharesHistory.Value;
    }

    private async Task RecheckTrustAsync(Channel channel, ChannelKeyRing ring)
    {
        var creators = await GetUserStatesAsync(ring.Records.Values
            .Where(r => !r.IsServerCreated)
            .Select(r => r.CreatorUserId));
        ring.Trusted.Clear();
        foreach (var record in ring.Records.Values)
        {
            if (!record.IsServerCreated && creators.TryGetValue(record.CreatorUserId, out var creator) &&
                await IsTrustedCreatorAsync(channel, ring, record, creator))
                ring.Trusted[record.Generation] = 0;
        }
    }

    /// <summary>
    /// Whether a device may send with a generation this user made. The key
    /// epoch of the device that signed it must be one the membership log
    /// admitted, or in a direct chat one the person accepted. Otherwise a
    /// faked key reset would let the server introduce a key it knows. Times
    /// on key records never go backwards, so a device removed from an account
    /// cannot date a new key before its removal.
    /// </summary>
    private async Task<bool> IsTrustedCreatorAsync(Channel channel, ChannelKeyRing ring,
        ChannelKeyGenerationRecord record, UserKeyState creator)
    {
        if (record.IsServerCreated || !creator.EverDevices.TryGetValue(record.CreatorDeviceId, out var device))
            return false;

        // A key made by a device that has since been removed, or replaced by
        // a reset, may be known to whoever holds that device now. The record
        // must also be dated within the device's time on the account.
        if (!creator.ActiveDevices.ContainsKey(record.CreatorDeviceId) ||
            creator.GetDeviceAt(record.CreatorDeviceId, record.Timestamp) is null)
            return false;

        // Without the previous record the device cannot check that this one
        // is not dated before it.
        if (record.Generation > 1 &&
            (!ring.Records.TryGetValue(record.Generation - 1, out var previous) ||
             record.TimestampMs < previous.TimestampMs))
            return false;

        var creatorId = record.CreatorUserId;
        switch (ring.Policy)
        {
            case ChannelKeyPolicy.Direct:
                if (creatorId != _client.Me.Id && creatorId != await DirectPeerAsync(channel))
                    return false;
                return creatorId == _client.Me.Id ||
                       device.Epoch <= (await GetAcceptedEpochAsync(creatorId) ?? creator.Epoch);

            case ChannelKeyPolicy.Group:
            case ChannelKeyPolicy.PlanetInviteOnly:
            {
                var log = await GovernedLogAsync(channel, ring.Policy);
                return log is not null && log.AdmitsDevice(creatorId, creator, device.Epoch);
            }

            case ChannelKeyPolicy.PlanetOpen:
                return true;

            default:
                return false;
        }
    }

    // The other person in each direct chat, as first seen on this device.
    private readonly ConcurrentDictionary<long, long> _directPeers = new();

    /// <summary>
    /// The other member of a direct chat. The first one this device sees is
    /// pinned, so the server cannot later swap in a different account and
    /// receive the chat's keys. Returns null when the chat's members do not
    /// match the pinned person.
    /// </summary>
    private async Task<long?> DirectPeerAsync(Channel channel)
    {
        var others = channel.Members?.Select(m => m.UserId).Where(id => id != _client.Me.Id).Distinct().ToList();
        if (channel.Members is not { Count: <= 2 } || others is not { Count: 1 })
            return null;

        var peer = others[0];
        if (!_directPeers.TryGetValue(channel.Id, out var pinned))
        {
            var bytes = await Store.GetAsync(StoreKey(DirectPeerKey(channel.Id)));
            if (bytes is { Length: 8 })
            {
                pinned = BitConverter.ToInt64(bytes);
            }
            else
            {
                pinned = peer;
                await Store.SetAsync(StoreKey(DirectPeerKey(channel.Id)), BitConverter.GetBytes(peer));
            }

            _directPeers[channel.Id] = pinned;
        }

        if (pinned == peer)
            return peer;

        LogWarning($"Direct chat {channel.Id} now lists a different person than this device first saw");
        return null;
    }

    internal static string DirectPeerKey(long channelId) => $"dm-peer/{channelId}";

    private Task<AccessLogState> GovernedLogAsync(Channel channel, ChannelKeyPolicy policy) => policy switch
    {
        ChannelKeyPolicy.Group => GetAccessLogStateAsync(AccessLogScope.GroupChannel, channel.Id, channel.Node),
        ChannelKeyPolicy.PlanetInviteOnly => GetAccessLogStateAsync(AccessLogScope.Planet, channel.PlanetId!.Value,
            channel.Node),
        _ => Task.FromResult<AccessLogState>(null)
    };

    // The newest generation each channel reached on this device, so the
    // server cannot hand out an older key that someone removed still holds.
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), int> _generationPins = new();

    private static string GenerationPinKey(Channel channel) => GenerationPinKey(channel.PlanetId ?? 0, channel.Id);

    private static string GenerationPinKey(long planetId, long channelId) => $"generation-pin/{planetId}/{channelId}";

    private async Task<int> GetGenerationPinAsync(Channel channel)
    {
        if (_generationPins.TryGetValue(ChannelKey(channel), out var pinned))
            return pinned;

        var bytes = await Store.GetAsync(StoreKey(GenerationPinKey(channel)));
        pinned = bytes is { Length: 4 } ? BitConverter.ToInt32(bytes) : 0;
        return _generationPins.AddOrUpdate(ChannelKey(channel), pinned, (_, current) => Math.Max(current, pinned));
    }

    private async Task PinGenerationAsync(Channel channel, int generation)
    {
        if (generation <= await GetGenerationPinAsync(channel))
            return;

        _generationPins.AddOrUpdate(ChannelKey(channel), generation, (_, current) => Math.Max(current, generation));
        await Store.SetAsync(StoreKey(GenerationPinKey(channel)), BitConverter.GetBytes(generation));
    }

    /// <summary>
    /// The policy the server reports, unless this device already verified a
    /// signed access log for the channel's planet or group. Such a channel
    /// stays governed by that log, so the server cannot relabel it as open
    /// and have members hand keys to accounts no admin admitted.
    /// </summary>
    private async Task<ChannelKeyPolicy> EffectivePolicyAsync(Channel channel, ChannelKeyPolicy reported)
    {
        var (scope, scopeId, governed) = channel.PlanetId is { } planetId
            ? (AccessLogScope.Planet, planetId, ChannelKeyPolicy.PlanetInviteOnly)
            : (AccessLogScope.GroupChannel, channel.Id, ChannelKeyPolicy.Group);

        // The planet itself and the channel's keys are reported separately;
        // if either says invite-only, the channel is governed.
        if (channel.PlanetId is not null && channel.Planet?.EncryptionMode == PlanetEncryptionMode.InviteOnly)
            reported = governed;

        // Loading the log verifies and pins it, so this device keeps
        // enforcing it even if the server later reports a weaker policy.
        if (reported == governed)
        {
            await GetAccessLogStateAsync(scope, scopeId, channel.Node);
            return reported;
        }

        if (!await HasAccessPinAsync(scope, scopeId))
            return reported;

        LogWarning($"Channel {channel.Id} was reported as {reported}, but it has a verified membership log");
        return governed;
    }

    /// <summary>
    /// Decides whether a user may hold this channel's keys. The rules are
    /// enforced on this device because the server is not trusted to make
    /// them: a one-to-one DM has exactly its two members, group DMs and
    /// invite-only planets follow their signed access logs, and open planets
    /// follow the planet's permissions as the server reports them.
    /// </summary>
    private async Task<bool> IsAuthorizedMemberAsync(Channel channel, ChannelKeyPolicy policy, long userId,
        UserKeyState state)
    {
        switch (policy)
        {
            case ChannelKeyPolicy.Direct:
                if (userId != _client.Me.Id && userId != await DirectPeerAsync(channel))
                    return false;

                // Keys go to the other person's new epoch only once this
                // person accepted their reset.
                return userId == _client.Me.Id ||
                       state.Epoch == (await GetAcceptedEpochAsync(userId) ?? state.Epoch);

            case ChannelKeyPolicy.Group:
            {
                var log = await GetAccessLogStateAsync(AccessLogScope.GroupChannel, channel.Id, channel.Node);
                return log is not null && log.IsMember(userId, state);
            }

            case ChannelKeyPolicy.PlanetInviteOnly:
            {
                var log = await GetAccessLogStateAsync(AccessLogScope.Planet, channel.PlanetId!.Value, channel.Node);
                return log is not null && log.IsMember(userId, state);
            }

            case ChannelKeyPolicy.PlanetOpen:
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Returns the secret for a generation, or null if this device does not
    /// have it. A missing key is requested from other members.
    /// </summary>
    public async Task<ChannelKeySecret> GetSecretAsync(Channel channel, int generation)
    {
        var ring = await GetKeyRingAsync(channel);
        if (ring is null)
            return null;

        if (!ring.Secrets.TryGetValue(generation, out var secret))
        {
            if (generation > ring.LatestGeneration)
            {
                ring = await GetKeyRingAsync(channel, refresh: true);
                ring?.Secrets.TryGetValue(generation, out secret);
            }
            else
            {
                // A later generation this device holds may unlock it. Only
                // the recent records come with the channel's keys, so the ones
                // in between are loaded now.
                var above = ring.Secrets.Keys.Where(g => g > generation).DefaultIfEmpty(0).Min();
                if (above > 0)
                {
                    await EnsureRecordsAsync(channel, ring, generation, above);
                    ring.Secrets.TryGetValue(generation, out secret);
                }
            }
        }

        if (secret is null)
            await RequestKeysAsync(channel);

        return secret;
    }

    /// <summary>
    /// Returns the secret whose index key produced the search terms of
    /// messages in a generation, or null if this device does not have it.
    /// </summary>
    public async Task<ChannelKeySecret> GetIndexSecretAsync(Channel channel, int generation)
    {
        var ring = await GetKeyRingAsync(channel);
        if (ring is null)
            return null;

        if (!ring.Records.ContainsKey(generation))
            await EnsureRecordsAsync(channel, ring, generation, generation);

        return ring.Records.TryGetValue(generation, out var record)
            ? await GetSecretAsync(channel, record.IndexGeneration)
            : null;
    }

    /// <summary>
    /// Asks online members to share the channel's newest key with this device.
    /// </summary>
    public async Task RequestKeysAsync(Channel channel)
    {
        if (Status != E2eeStatus.Ready)
            return;

        var now = DateTime.UtcNow;
        if (_lastKeyRequests.TryGetValue(ChannelKey(channel), out var last) && now - last < KeyRequestInterval)
            return;
        _lastKeyRequests[ChannelKey(channel)] = now;

        var result = await channel.Node.PostAsync(ChannelRoute(channel, "key-requests"), (object)null);
        if (!result.Success)
            LogWarning($"Key request for channel {channel.Id} failed: {result.Message}");
    }

    /// <summary>
    /// Returns the key to send with, creating or rotating the channel key
    /// when needed.
    /// </summary>
    public async Task<TaskResult<ChannelKeySecret>> EnsureSendKeyAsync(Channel channel)
    {
        if (Status != E2eeStatus.Ready)
            return TaskResult<ChannelKeySecret>.FromFailure("Verify this device to send encrypted messages.");

        var ring = await GetKeyRingAsync(channel, refresh: channel.EncryptionGeneration == 0);
        if (ring is null)
            return TaskResult<ChannelKeySecret>.FromFailure("Could not load this channel's encryption keys.");

        if (ring.RolledBack)
            return TaskResult<ChannelKeySecret>.FromFailure(
                "The server returned an older key for this channel than this device already saw, so sending is paused. Try again later.");

        if (ring.Policy == ChannelKeyPolicy.Direct)
        {
            if (await DirectPeerAsync(channel) is not { } peer)
                return TaskResult<ChannelKeySecret>.FromFailure(
                    "This chat's members changed since this device last saw it, so sending is paused.");
            if (await IsKeyResetPendingAsync(peer))
                return TaskResult<ChannelKeySecret>.FromFailure(
                    "This person's security code changed. Check it's really them before sending.");
        }

        if (ring.LatestGeneration == 0)
        {
            var created = await EnableChannelEncryptionAsync(channel);
            if (!created.Success)
                return TaskResult<ChannelKeySecret>.FromFailure(created.Message);
            ring = await GetKeyRingAsync(channel);
            if (ring is null)
                return TaskResult<ChannelKeySecret>.FromFailure("Could not load this channel's encryption keys.");
        }

        // A member without the current key asks for it. If nobody online
        // shares it soon, they start a new key sealed to the other members, so
        // they can send right away and read earlier messages once someone who
        // holds the old key comes online.
        if (ring.Latest is null)
        {
            ring = await WaitForSharedKeyAsync(channel) ?? ring;
            if (ring.Latest is null)
            {
                var started = await StartKeyWithoutEarlierKeysAsync(channel, ring);
                if (!started.Success)
                    return TaskResult<ChannelKeySecret>.FromFailure(started.Message);
                ring = await GetKeyRingAsync(channel);
                if (ring is null)
                    return TaskResult<ChannelKeySecret>.FromFailure("Could not load this channel's encryption keys.");
            }
        }

        // A record that could not be checked, for example because its
        // creator's keys failed to load, is retried rather than replaced.
        if (ring.LatestRecord is null)
        {
            ring = await GetKeyRingAsync(channel, refresh: true);
            if (ring?.LatestRecord is null)
                return TaskResult<ChannelKeySecret>.FromFailure(
                    "This channel's current key could not be verified. Try again in a moment.");
        }

        if (await NeedsNewKeyAsync(channel, ring) is { } reason)
        {
            // The server knew a key it created, including its search key, so
            // members replace both before sending anything.
            var serverCreated = ring.LatestRecord?.IsServerCreated == true;
            var rotated = await RotateChannelKeyAsync(channel,
                serverCreated ? ChannelKeyRotationReason.Initial : ChannelKeyRotationReason.MemberRemoved,
                rotateIndex: serverCreated);
            if (!rotated.Success)
                return TaskResult<ChannelKeySecret>.FromFailure(rotated.Message);
            ring = await GetKeyRingAsync(channel);
            if (ring is null || !ring.Trusted.ContainsKey(ring.LatestGeneration))
                return TaskResult<ChannelKeySecret>.FromFailure(
                    $"This channel's key could not be replaced ({reason}). Try again later.");
        }

        return ring.Latest is { } latest
            ? TaskResult<ChannelKeySecret>.FromData(latest)
            : TaskResult<ChannelKeySecret>.FromFailure("This device does not hold the channel's current key.");
    }

    /// <summary>
    /// Why the newest key must be replaced before sending, or null when it can
    /// be used. The device decides this from signed data where it can, so the
    /// server cannot keep members on a key it knows or someone removed holds.
    /// </summary>
    private async Task<string> NeedsNewKeyAsync(Channel channel, ChannelKeyRing ring)
    {
        var record = ring.LatestRecord;
        if (record.IsServerCreated)
            return "the server created it";
        if (!ring.Trusted.ContainsKey(ring.LatestGeneration))
            return "it was made with keys this device does not accept";
        if (ring.RotationRequired)
            return "someone lost access";

        if (ring.IsGoverned)
        {
            var log = await GovernedLogAsync(channel, ring.Policy);
            if (log is null)
                return null;
            if (log.LastRemovalSeq > record.Terms.AccessLogSeq)
                return "someone was removed from the membership log";

            // A creator cannot claim a log position this device has not seen.
            if (record.Terms.AccessLogSeq > log.HeadSeq)
                return "it names a membership log entry this device has not seen";
        }

        // A device removed, or keys reset, after the key was made may still
        // hold it, even if the server does not ask for a new one. Each such
        // change causes one new key.
        var changedAt = await LatestKeyRemovalAsync(channel, ring);
        if (changedAt > record.TimestampMs &&
            (!_rotatedForRemoval.TryGetValue(ChannelKey(channel), out var handled) || handled < changedAt))
        {
            _rotatedForRemoval[ChannelKey(channel)] = changedAt;
            return "a member removed a device or reset their keys";
        }

        return null;
    }

    // The latest device removal each channel already rotated for.
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), long> _rotatedForRemoval = new();

    /// <summary>
    /// When a member of a direct or group chat, or this user, last removed a
    /// device or reset their keys. Large invite-only planets rely on the
    /// server's rotation flag for other members.
    /// </summary>
    private async Task<long> LatestKeyRemovalAsync(Channel channel, ChannelKeyRing ring)
    {
        var users = new HashSet<long> { _client.Me.Id };
        if (ring.Policy == ChannelKeyPolicy.Direct && await DirectPeerAsync(channel) is { } peer)
            users.Add(peer);
        else if (ring.Policy == ChannelKeyPolicy.Group && channel.Members is not null)
            users.UnionWith(channel.Members.Select(m => m.UserId));

        var states = await GetUserStatesAsync(users);
        return states.Values
            .SelectMany(s => s.Records)
            .Where(r => r.Type is UserKeyLogEntryType.RevokeDevice or UserKeyLogEntryType.Reset)
            .Select(r => r.TimestampMs)
            .DefaultIfEmpty(0)
            .Max();
    }

    private async Task<ChannelKeyRing> WaitForSharedKeyAsync(Channel channel)
    {
        await RequestKeysAsync(channel);

        ChannelKeyRing ring = null;
        var waited = TimeSpan.Zero;
        var step = TimeSpan.FromSeconds(1);
        while (waited < KeyShareWait)
        {
            await Task.Delay(step);
            waited += step;
            ring = await GetKeyRingAsync(channel, refresh: true);
            if (ring?.Latest is not null)
                return ring;
        }

        return ring;
    }

    private async Task<TaskResult> StartKeyWithoutEarlierKeysAsync(Channel channel, ChannelKeyRing ring)
    {
        // A planet that hides history from new members would start such a key
        // for them anyway.
        var reason = ring.SharesHistory
            ? ChannelKeyRotationReason.KeyUnavailable
            : ChannelKeyRotationReason.NewMemberWithoutHistory;

        var created = await CreateGenerationAsync(channel, ring, reason, historyBreak: true, newIndex: true);
        if (created.Success)
            return created;

        // Another member may have shared or started a key at the same moment.
        var refreshed = await GetKeyRingAsync(channel, refresh: true);
        return refreshed?.Latest is not null ? TaskResult.SuccessResult : created;
    }

    /// <summary>
    /// Turns on end-to-end encryption for a channel by publishing its first key.
    /// </summary>
    public async Task<TaskResult> EnableChannelEncryptionAsync(Channel channel)
    {
        if (Status != E2eeStatus.Ready)
            return Fail("Verify this device first.");

        var ring = await GetKeyRingAsync(channel, refresh: true);
        if (ring is null)
            return Fail("Could not load this channel's encryption keys.");
        if (ring.LatestGeneration > 0)
            return TaskResult.SuccessResult;

        if (ring.Policy == ChannelKeyPolicy.Group)
        {
            var log = await EnsureGroupAccessLogAsync(channel);
            if (!log.Success)
                return log;
            ring = await GetKeyRingAsync(channel, refresh: true);
        }

        var created = await CreateGenerationAsync(channel, ring, ChannelKeyRotationReason.Initial, historyBreak: false,
            newIndex: true);
        if (created.Success)
            return created;

        // Another member may have published the first key at the same moment.
        var refreshed = await GetKeyRingAsync(channel, refresh: true);
        return refreshed?.LatestGeneration > 0 ? TaskResult.SuccessResult : created;
    }

    /// <summary>
    /// Replaces the channel key so anyone who lost access cannot read later
    /// messages. The new key unlocks the old one for everyone who keeps access.
    /// </summary>
    /// <param name="sharesHistory">
    /// A new history setting chosen by the planet owner in its settings. Other
    /// rotations carry the setting of the newest trusted key forward.
    /// </param>
    public async Task<TaskResult> RotateChannelKeyAsync(Channel channel, ChannelKeyRotationReason reason,
        bool rotateIndex = false, bool? sharesHistory = null)
    {
        var ring = await GetKeyRingAsync(channel, refresh: true);
        if (ring?.Latest is null)
            return Fail("This device does not hold the channel's current key.");

        var result = await CreateGenerationAsync(channel, ring, reason,
            historyBreak: reason == ChannelKeyRotationReason.NewMemberWithoutHistory, newIndex: rotateIndex,
            sharesHistory: sharesHistory);

        // Moderators can recompute automod terms for a new index key right away.
        if (result.Success && rotateIndex && channel.PlanetId is not null)
            _ = SyncAutomodTermsIfModeratorAsync(channel.PlanetId.Value);

        return result;
    }

    private async Task<TaskResult> CreateGenerationAsync(Channel channel, ChannelKeyRing ring,
        ChannelKeyRotationReason reason, bool historyBreak, bool newIndex, bool? sharesHistory = null)
    {
        var generation = ring.LatestGeneration + 1;
        ChannelKeySecret previous = null;
        if (generation > 1 && !historyBreak)
        {
            previous = ring.Latest;
            if (previous is null)
                return Fail("This device does not hold the channel's current key.");
        }

        // A search key the server created is never reused by members, and a
        // key that does not unlock the previous one needs its own search key,
        // because members who only hold it cannot reach the earlier one.
        var currentIndex = generation == 1 ? 0 : ring.Records[ring.LatestGeneration].IndexGeneration;
        if (currentIndex > 0 && !ring.Records.ContainsKey(currentIndex))
            await EnsureRecordsAsync(channel, ring, currentIndex, currentIndex);
        var serverIndex = currentIndex > 0 && ring.Records.TryGetValue(currentIndex, out var indexRecord) &&
                          indexRecord.IsServerCreated;
        var indexGeneration = generation == 1 || newIndex || historyBreak || serverIndex ? generation : currentIndex;

        // The record carries the rules it was made under, and the newest
        // membership log entry this device verified, so other devices can tell
        // whether someone was removed after it was made. A governed channel
        // gets no new key while its log cannot be verified.
        var log = ring.IsGoverned ? await GovernedLogAsync(channel, ring.Policy) : null;
        if (ring.IsGoverned && log is null)
            return Fail("This channel's membership log could not be verified, so its key cannot be replaced yet. Try again later.");

        // In governed channels the history setting is carried forward from the
        // newest trusted key and only changes through the owner's settings.
        // Open planets follow what the server reports.
        var terms = new ChannelKeyTerms(ring.Policy,
            sharesHistory ?? (ring.IsGoverned ? ring.SharesHistory : ring.ReportedSharesHistory),
            log?.HeadSeq ?? -1);

        // Key record times never go backwards, even when this device's clock
        // is behind the one that made the previous key.
        var timestamp = Math.Max(NowMs(), (ring.LatestRecord?.TimestampMs ?? 0) + 1);

        var secret = ChannelKeySecret.Generate(channel.Id, generation);
        var (entry, record) = ChannelKeyGenerationBuilder.Create(secret, channel.PlanetId ?? 0, _client.Me.Id, Device,
            reason, indexGeneration, previous, timestamp, terms);

        // Members receive a new key as soon as it exists, so nobody has to
        // wait for a holder to come online. In very large planets the most
        // recently active members receive it and share it with the rest.
        var boxes = new List<ChannelKeyBoxDto> { SealBox(secret, _client.Me.Id, MyKeyState.UserKey) };
        foreach (var (userId, userKey) in await GetNewKeyRecipientsAsync(channel, ring))
            boxes.Add(SealBox(secret, userId, userKey));

        var result = await channel.Node.PostAsyncWithResponse<ChannelKeyStateDto>(ChannelRoute(channel, "generations"),
            new CreateChannelKeyGenerationRequest { Entry = entry, Boxes = boxes.Take(E2eeLimits.MaxBoxesPerRequest).ToList() });
        if (!result.Success)
        {
            _keyRings.TryRemove(ChannelKey(channel), out _);
            return Fail(result.Message);
        }

        // Boxes sealed to a user key its owner has since replaced, for example
        // after signing out a device, are left out by the server. Those
        // members are sealed to again with their current keys.
        var stale = new HashSet<long>(result.Data?.StaleRecipients ?? []);
        foreach (var chunk in boxes.Skip(E2eeLimits.MaxBoxesPerRequest).Chunk(100))
        {
            var shared = await channel.Node.PostAsyncWithResponse<ChannelKeyShareResultDto>(
                ChannelRoute(channel, "boxes"), chunk.ToList());
            if (!shared.Success)
            {
                LogWarning($"Sharing a new key in channel {channel.Id} failed: {shared.Message}");
                break;
            }

            stale.UnionWith(shared.Data?.StaleRecipients ?? []);
        }

        if (stale.Count > 0)
            await ShareWithRefreshedKeysAsync(channel, ring, secret, stale);

        ring.Records[generation] = record;
        ring.Secrets[generation] = secret;
        ring.Trusted[generation] = 0;
        ring.LatestGeneration = generation;
        ring.RotationRequired = false;
        channel.EncryptionGeneration = generation;
        await PinGenerationAsync(channel, generation);
        ChannelKeysChanged?.Invoke(channel.Id);
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Seals a key again to members whose keys changed since this device
    /// loaded them. It is tried once; members still missing it ask again.
    /// </summary>
    private async Task ShareWithRefreshedKeysAsync(Channel channel, ChannelKeyRing ring, ChannelKeySecret secret,
        IReadOnlyCollection<long> userIds)
    {
        foreach (var userId in userIds)
            _userStates.TryRemove(userId, out _);

        var states = await GetUserStatesAsync(userIds);
        var boxes = new List<ChannelKeyBoxDto>();
        foreach (var (userId, state) in states)
        {
            if (userId != _client.Me.Id && state.HasIdentity &&
                await IsAuthorizedMemberAsync(channel, ring.Policy, userId, state))
                boxes.Add(SealBox(secret, userId, state.UserKey));
        }

        foreach (var chunk in boxes.Chunk(100))
        {
            var shared = await channel.Node.PostAsync(ChannelRoute(channel, "boxes"), chunk.ToList());
            if (!shared.Success)
            {
                LogWarning($"Sharing a key again in channel {channel.Id} failed: {shared.Message}");
                return;
            }
        }
    }

    /// <summary>
    /// Members a new key is sealed to: the other members of a direct or group
    /// chat, or a planet channel's most recently active viewers, each checked
    /// against the channel's policy on this device.
    /// </summary>
    private async Task<List<(long UserId, UserPublicKey Key)>> GetNewKeyRecipientsAsync(Channel channel,
        ChannelKeyRing ring)
    {
        List<long> candidates;
        if (ring.Policy is ChannelKeyPolicy.Direct or ChannelKeyPolicy.Group)
        {
            candidates = channel.Members?.Select(m => m.UserId).Where(id => id != _client.Me.Id).ToList() ?? [];
        }
        else
        {
            var result = await channel.Node.GetJsonAsync<List<long>>(
                ChannelRouteWith(channel, "key-candidates") + $"limit={E2eeLimits.MaxKeyCandidates}",
                cacheDurationMs: null);
            candidates = result.Success ? result.Data ?? [] : [];
        }

        var recipients = new List<(long, UserPublicKey)>();
        var states = await GetUserStatesAsync(candidates);
        foreach (var userId in candidates)
        {
            if (userId != _client.Me.Id && states.TryGetValue(userId, out var state) && state.HasIdentity &&
                await IsAuthorizedMemberAsync(channel, ring.Policy, userId, state))
                recipients.Add((userId, state.UserKey));
        }

        return recipients;
    }

    private static ChannelKeyBoxDto SealBox(ChannelKeySecret secret, long userId, UserPublicKey userKey) => new()
    {
        ChannelId = secret.ChannelId,
        Generation = secret.Generation,
        UserId = userId,
        UserKeyGeneration = userKey.Generation,
        Box = secret.SealTo(userId, userKey)
    };

    // Sharing keys with members who ask

    private void ScheduleServe(long channelId, long? planetId)
    {
        if (Status != E2eeStatus.Ready || !_scheduledServes.TryAdd(ChannelKey(channelId, planetId), 0))
            return;

        // A short random delay spreads the work among the members who hold the
        // key; whoever answers first wins and the rest find nothing to do.
        var delay = TimeSpan.FromMilliseconds(Random.Shared.Next(250, 2500));
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay);
            _scheduledServes.TryRemove(ChannelKey(channelId, planetId), out _);
            try
            {
                var channel = await ResolveChannelAsync(channelId, planetId);
                if (channel is not null)
                    await ServeChannelAsync(channel);
            }
            catch (Exception e)
            {
                LogError($"Failed to share keys for channel {channelId}", e);
            }
        });
    }

    /// <summary>
    /// Seals the channel's newest key for every waiting member this device
    /// can confirm is allowed to have it.
    /// </summary>
    public async Task ServeChannelAsync(Channel channel)
    {
        if (Status != E2eeStatus.Ready)
            return;

        var recipients = await channel.Node.GetJsonAsync<ChannelKeyRecipientsDto>(ChannelRoute(channel, "recipients"),
            cacheDurationMs: null);
        if (!recipients.Success || recipients.Data is null || recipients.Data.Pending.Count == 0)
            return;

        var ring = await GetKeyRingAsync(channel, refresh: true);
        if (ring is null || ring.LatestGeneration != recipients.Data.LatestGeneration)
            return;

        var states = await GetUserStatesAsync(recipients.Data.Pending.Select(p => p.UserId));
        var authorized = new List<(PendingKeyRecipientDto Recipient, UserKeyState State)>();
        foreach (var pending in recipients.Data.Pending)
        {
            if (pending.UserId == _client.Me.Id || !states.TryGetValue(pending.UserId, out var state) || !state.HasIdentity)
                continue;
            if (await IsAuthorizedMemberAsync(channel, ring.Policy, pending.UserId, state))
                authorized.Add((pending, state));
        }

        // Without shared history only the newest key is ever given out, so a
        // member who only lacks an earlier key gets nothing and causes no new key.
        var sharedLatest = recipients.Data.LatestGeneration;
        if (!ring.SharesHistory)
            authorized = authorized.Where(a => a.Recipient.Generations.Contains(sharedLatest)).ToList();

        if (authorized.Count == 0)
            return;

        // The policy the recipients were checked against. If loading keys
        // below reveals a stricter one, nothing is shared this time.
        var checkedPolicy = ring.Policy;

        // Channels that hide history give newcomers a key that cannot unlock
        // anything sent before they arrived. The newest key already is one if
        // it does not unlock earlier keys and no message uses it yet.
        // In an invite-only planet the server could claim a used key is unused
        // or that a newcomer already had earlier keys, so each newcomer there
        // gets a new key. Open planets trust the server with membership anyway.
        var trustServerClaims = !ring.IsGoverned;
        if (!ring.SharesHistory && ring.Latest is not null &&
            !(trustServerClaims && recipients.Data.LatestIsUnused) &&
            (!trustServerClaims || authorized.Any(a => !a.Recipient.HasEarlierKeys)))
        {
            var rotated = await RotateChannelKeyAsync(channel, ChannelKeyRotationReason.NewMemberWithoutHistory);
            if (!rotated.Success)
                return;
            ring = await GetKeyRingAsync(channel);
        }

        // Each member receives every missing key this device holds: the newest
        // one and the heads of earlier chains they cannot reach.
        var boxes = new List<ChannelKeyBoxDto>();
        foreach (var (recipient, state) in authorized)
        {
            // Without shared history only the newest key is given out. A key
            // that unlocks earlier ones only goes to members who already held
            // those, which only open planets take the server's word for.
            IEnumerable<int> generations = ring.SharesHistory
                ? recipient.Generations.Select(g => g == sharedLatest ? ring.LatestGeneration : g).Distinct()
                : ring.LatestRecord is { UnlocksPrevious: false } || (trustServerClaims && recipient.HasEarlierKeys)
                    ? [ring.LatestGeneration]
                    : [];
            foreach (var generation in generations)
            {
                var secret = ring.Secrets.GetValueOrDefault(generation) ?? await GetSecretAsync(channel, generation);
                if (secret is not null)
                    boxes.Add(SealBox(secret, recipient.UserId, state.UserKey));
            }
        }

        if (boxes.Count == 0 || _keyRings.GetValueOrDefault(ChannelKey(channel))?.Policy is { } current &&
            current != checkedPolicy)
            return;

        foreach (var chunk in boxes.Chunk(100))
        {
            var result = await channel.Node.PostAsyncWithResponse<ChannelKeyShareResultDto>(
                ChannelRoute(channel, "boxes"), chunk.ToList());
            if (!result.Success)
            {
                LogWarning($"Sharing keys in channel {channel.Id} failed: {result.Message}");
                return;
            }

            // Members whose keys changed are loaded afresh the next time they
            // ask, which they do while they still lack the key.
            foreach (var userId in result.Data?.StaleRecipients ?? [])
                _userStates.TryRemove(userId, out _);
        }
    }

    /// <summary>
    /// Serves key requests in direct and group chats that arrived while this
    /// device was offline.
    /// </summary>
    private async Task ServePendingDirectRequestsAsync()
    {
        try
        {
            var requests = await HubNode.GetJsonAsync<List<KeyRequestDto>>("api/e2ee/key-requests",
                cacheDurationMs: null);
            if (!requests.Success || requests.Data is null)
                return;

            foreach (var channelId in requests.Data.Select(r => r.ChannelId).Distinct())
            {
                var channel = await ResolveChannelAsync(channelId, null);
                if (channel is not null)
                    await ServeChannelAsync(channel);
            }
        }
        catch (Exception e)
        {
            LogError("Failed to serve pending key requests", e);
        }
    }

    private async Task OnKeysAvailableAsync(long channelId, long? planetId)
    {
        // The request throttle is kept, so a message that stays unreadable
        // does not ask again the moment any new key arrives.
        _keyRings.TryRemove(ChannelKey(channelId, planetId), out _);
        _keyRingFailures.TryRemove(ChannelKey(channelId, planetId), out _);
        ChannelKeysChanged?.Invoke(channelId);
        await RetryPendingMessagesAsync(channelId, planetId);
    }
}
