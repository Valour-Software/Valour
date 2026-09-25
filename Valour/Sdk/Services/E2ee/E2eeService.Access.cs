using System.Collections.Concurrent;
using System.Text.Json;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

/// <summary>
/// An invite for a private planet whose secret travels in the link's
/// fragment, which browsers never send to the server.
/// </summary>
public sealed class EncryptedInvite
{
    public string InviteId { get; init; }
    public byte[] Secret { get; init; }

    /// <summary>The text to append to an invite link, starting with '#'.</summary>
    public string Fragment => $"#e2ee={InviteId}.{Base64Url.Encode(Secret)}";

    public static bool TryParseFragment(string fragment, out EncryptedInvite invite)
    {
        invite = null;
        if (string.IsNullOrWhiteSpace(fragment))
            return false;

        var value = fragment.TrimStart('#');
        foreach (var part in value.Split('&'))
        {
            if (!part.StartsWith("e2ee=", StringComparison.Ordinal))
                continue;

            var pieces = part["e2ee=".Length..].Split('.');
            if (pieces.Length != 2 || pieces[0].Length is 0 or > 64)
                return false;

            try
            {
                var secret = Base64Url.Decode(pieces[1]);
                if (secret.Length != 16)
                    return false;
                invite = new EncryptedInvite { InviteId = pieces[0], Secret = secret };
                return true;
            }
            catch (E2eeFormatException)
            {
                return false;
            }
        }

        return false;
    }
}

/// <summary>
/// A member of a private planet or encrypted group who is waiting on an
/// admin, either to be admitted or to have their new keys confirmed.
/// </summary>
public sealed class PendingAdmission
{
    public long UserId { get; init; }

    /// <summary>True when the member was admitted before but their keys changed since.</summary>
    public bool KeysChanged { get; init; }

    /// <summary>The member's current key epoch.</summary>
    public int Epoch { get; init; }

    /// <summary>
    /// The member's security number when the list was loaded, or null when
    /// they have not set up encryption yet. Nobody can be admitted without
    /// keys; they stay on the list until they sign in and set them up. Pass
    /// this number to <see cref="E2eeService.ConfirmMemberKeysAsync"/> after
    /// comparing it with the person.
    /// </summary>
    public string SafetyNumber { get; init; }

    public bool HasKeys => SafetyNumber is not null;
}

public partial class E2eeService
{
    /// <summary>
    /// How many entries an owner's or admin's device lets a membership log
    /// grow past its last checkpoint before it signs a new one. New devices
    /// start reading from the latest checkpoint, so this bounds how much of
    /// the log they fetch and verify.
    /// </summary>
    public int AccessLogCheckpointInterval { get; set; } = 256;

    /// <summary>
    /// The newest entry of an access log this device has verified. Later logs
    /// must extend it, so the server cannot hide the log, roll it back to undo
    /// a removal, or swap in a different one.
    /// </summary>
    private sealed class AccessPin
    {
        public int Count { get; set; }
        public string HeadHash { get; set; }
    }

    private Dictionary<string, AccessPin> _accessPins;

    private static string AccessPinKey(AccessLogScope scope, long scopeId) => $"{(int)scope}:{scopeId}";

    private string AccessStateKey(AccessLogScope scope, long scopeId) =>
        StoreKey("access-state/" + AccessPinKey(scope, scopeId));

    private async Task<AccessPin> GetAccessPinAsync(AccessLogScope scope, long scopeId)
    {
        await _pinLock.WaitAsync();
        try
        {
            _accessPins ??= await LoadAccessPinsAsync();
            var key = AccessPinKey(scope, scopeId);
            if (_accessPins.TryGetValue(key, out var pin))
                return pin;

            // Another tab or process sharing the store may have verified the log since.
            if ((await LoadAccessPinsAsync()).TryGetValue(key, out pin))
                _accessPins[key] = pin;
            return pin;
        }
        finally
        {
            _pinLock.Release();
        }
    }

    /// <summary>
    /// True when this device has seen a signed access log for the scope. Its
    /// channels stay governed by that log whatever policy the server reports.
    /// </summary>
    private async Task<bool> HasAccessPinAsync(AccessLogScope scope, long scopeId) =>
        await GetAccessPinAsync(scope, scopeId) is not null;

    /// <summary>
    /// Stores a verified state and pins its head. The stored state lets this
    /// device fetch only entries after it next time. Pins another tab or
    /// process stored are kept, and a scope keeps whichever pin is further
    /// along, since both extend the same log.
    /// </summary>
    private async Task SaveAccessLogAsync(AccessLogState state)
    {
        await _pinLock.WaitAsync();
        try
        {
            _accessPins ??= await LoadAccessPinsAsync();
            foreach (var (key, stored) in await LoadAccessPinsAsync())
            {
                if (!_accessPins.TryGetValue(key, out var mine) || stored.Count > mine.Count)
                    _accessPins[key] = stored;
            }

            var scopeKey = AccessPinKey(state.Scope, state.ScopeId);
            if (!_accessPins.TryGetValue(scopeKey, out var current) || state.HeadSeq + 1 >= current.Count)
            {
                _accessPins[scopeKey] = new AccessPin
                {
                    Count = state.HeadSeq + 1,
                    HeadHash = Base64Url.Encode(state.HeadHash)
                };
                // The stored state only saves fetching the log again, so a full
                // store does not stop the log from being used. The pin is kept.
                try
                {
                    await Store.SetAsync(AccessStateKey(state.Scope, state.ScopeId), state.Encode());
                }
                catch (Exception e)
                {
                    LogWarning($"Saving access log {state.Scope}/{state.ScopeId} on this device failed: {e.Message}");
                }
            }

            await SaveAccessPinsAsync();
        }
        finally
        {
            _pinLock.Release();
        }
    }

    private async Task<AccessLogState> LoadStoredAccessLogAsync(AccessLogScope scope, long scopeId)
    {
        var bytes = await Store.GetAsync(AccessStateKey(scope, scopeId));
        if (bytes is null)
            return null;

        try
        {
            var state = AccessLogState.Decode(bytes);
            return state.Scope == scope && state.ScopeId == scopeId ? state : null;
        }
        catch (E2eeFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The cutoffs that removing one of this account's devices signs into its
    /// key log (see <see cref="AccessLogCutoff"/>), one for each membership
    /// log the account belongs to: private planets it joined or owns, and
    /// group chats. When this device removes itself, each log it verified is
    /// cut off at its pin, which includes every entry it signed, and every
    /// other log at -1, since it signs only in logs it verified. When it
    /// removes another device, each log is loaded so the cutoff includes what
    /// that device signed. A log that cannot be loaded is left out, and there
    /// the removed device is checked only against the removal time.
    /// </summary>
    private async Task<List<AccessLogCutoff>> CollectAccessLogCutoffsAsync(bool removingThisDevice)
    {
        var cutoffs = new Dictionary<(AccessLogScope Scope, long ScopeId), int>();
        await _pinLock.WaitAsync();
        try
        {
            // Another tab or process may have signed and pinned newer entries.
            _accessPins ??= await LoadAccessPinsAsync();
            foreach (var (key, stored) in await LoadAccessPinsAsync())
            {
                if (!_accessPins.TryGetValue(key, out var mine) || stored.Count > mine.Count)
                    _accessPins[key] = stored;
            }

            foreach (var (key, pin) in _accessPins)
            {
                if (TryParseAccessPinKey(key, out var scope, out var scopeId))
                    cutoffs[(scope, scopeId)] = pin.Count - 1;
            }
        }
        finally
        {
            _pinLock.Release();
        }

        var memberScopes = await GetMemberAccessLogScopesAsync();

        if (removingThisDevice)
        {
            foreach (var scope in memberScopes)
                cutoffs.TryAdd(scope, -1);
        }
        else
        {
            foreach (var (scope, scopeId) in cutoffs.Keys.Concat(memberScopes).ToHashSet())
            {
                cutoffs.Remove((scope, scopeId));
                try
                {
                    var node = scope == AccessLogScope.Planet
                        ? (await _client.PlanetService.FetchPlanetAsync(scopeId))?.Node
                        : HubNode;
                    var log = node is null ? null : await GetAccessLogStateAsync(scope, scopeId, node, refresh: true);
                    if (log is not null)
                        cutoffs[(scope, scopeId)] = log.HeadSeq;
                }
                catch (Exception e)
                {
                    LogWarning($"Access log {scope}/{scopeId} could not be loaded for a device removal: {e.Message}");
                }
            }
        }

        if (cutoffs.Count > AccessLogCutoff.MaxPerEntry)
            LogWarning($"This account belongs to {cutoffs.Count} membership logs; the removal lists the first " +
                       $"{AccessLogCutoff.MaxPerEntry}, and the rest are checked only against the removal time.");

        return cutoffs
            .Take(AccessLogCutoff.MaxPerEntry)
            .Select(c => new AccessLogCutoff(c.Key.Scope, c.Key.ScopeId, c.Value))
            .ToList();
    }

    /// <summary>
    /// The membership logs this account may belong to: private planets it
    /// joined or owns, and its group chats. The lists are read from the server
    /// without changing the app's cached ones, which may not be loaded; the
    /// cached lists are used when the server cannot be reached.
    /// </summary>
    private async Task<HashSet<(AccessLogScope Scope, long ScopeId)>> GetMemberAccessLogScopesAsync()
    {
        var planets = await HubNode.GetJsonAsync<List<Planet>>("api/users/me/planets", cacheDurationMs: null);
        var channels = await HubNode.GetJsonAsync<List<Channel>>("api/channels/direct/self", cacheDurationMs: null);

        IEnumerable<ISharedPlanet> planetList = planets.Success && planets.Data is not null
            ? planets.Data
            : _client.PlanetService.JoinedPlanets;
        IEnumerable<ISharedChannel> channelList = channels.Success && channels.Data is not null
            ? channels.Data
            : _client.ChannelService.DirectChatChannels;

        return planetList
            .Where(p => p.EncryptionMode == PlanetEncryptionMode.InviteOnly || p.OwnerId == _client.Me.Id)
            .Select(p => (AccessLogScope.Planet, p.Id))
            .Concat(channelList
                .Where(c => c.ChannelType == ChannelTypeEnum.GroupChat)
                .Select(c => (AccessLogScope.GroupChannel, c.Id)))
            .ToHashSet();
    }

    private Task<Dictionary<string, AccessPin>> LoadAccessPinsAsync() =>
        LoadStoredJsonAsync<Dictionary<string, AccessPin>>(AccessPinsKey);

    private Task SaveAccessPinsAsync() =>
        Store.SetAsync(StoreKey(AccessPinsKey), JsonSerializer.SerializeToUtf8Bytes(_accessPins));

    private readonly ConcurrentDictionary<(AccessLogScope, long), AccessLogState> _accessLogs = new();
    private readonly ConcurrentDictionary<(AccessLogScope, long), byte> _checkpointsInProgress = new();

    private static string AccessLogRoute(AccessLogScope scope, long scopeId) =>
        $"api/e2ee/access-logs/{(int)scope}/{scopeId}";

    /// <summary>
    /// Returns the verified access log for a private planet or group DM, or
    /// null when it has none. A planet's log stays after the owner makes the
    /// planet public; <see cref="AccessLogState.IsOpen"/> is then true.
    ///
    /// A device that verified the log before continues from the state it
    /// stored and applies only newer entries, so a gap or a rewritten entry
    /// fails verification. A device with nothing stored starts from the latest
    /// checkpoint. One that has a pin but no stored state also starts from the
    /// latest checkpoint and checks the pinned entry; when the pin is older
    /// than the checkpoint it reads the whole log to check it.
    /// </summary>
    public async Task<AccessLogState> GetAccessLogStateAsync(AccessLogScope scope, long scopeId, Node node,
        bool refresh = false)
    {
        _accessLogs.TryGetValue((scope, scopeId), out var known);
        if (known is not null && !refresh)
            return known;

        known ??= await LoadStoredAccessLogAsync(scope, scopeId);
        var pin = known is null ? await GetAccessPinAsync(scope, scopeId) : null;
        var from = known is null ? 0 : known.HeadSeq + 1;
        var route = $"{AccessLogRoute(scope, scopeId)}?known={from}";
        var result = await node.GetJsonAsync<List<AccessLogEntry>>(route, cacheDurationMs: null);
        if (!result.Success)
            return known;

        var entries = result.Data ?? [];
        if (entries.Count == 0)
        {
            if (known is not null)
            {
                _accessLogs[(scope, scopeId)] = known;
                return known;
            }

            if (pin is not null)
                LogError($"Access log {scope}/{scopeId} is missing, but this device verified it before");
            return null;
        }

        var states = await GetUserStatesAsync(AccessLogVerifier.RequiredUsers(entries));
        try
        {
            AccessLogState state;
            if (known is not null)
            {
                state = AccessLogState.Decode(known.Encode());
                foreach (var entry in entries)
                    AccessLogVerifier.Apply(state, entry, states);
            }
            else
            {
                // A checkpoint a removed device signed after its cutoff
                // cannot start the log, so the whole log is read instead.
                if (!AccessLogVerifier.CanStartFrom(entries, states))
                {
                    entries = await FetchWholeAccessLogAsync(scope, scopeId, node, entries[^1].Seq);
                    if (entries is null)
                        return null;
                    states = await GetUserStatesAsync(AccessLogVerifier.RequiredUsers(entries));
                }

                state = AccessLogVerifier.Verify(scope, scopeId, entries, states);
                if (pin is not null && !await MatchesPinAsync(scope, scopeId, node, entries, state, pin))
                {
                    LogError($"Access log {scope}/{scopeId} conflicts with the one this device verified before");
                    return null;
                }
            }

            await SaveAccessLogAsync(state);
            _accessLogs[(scope, scopeId)] = state;

            // Channel keys loaded under the planet's earlier privacy follow
            // the wrong policy, so they are loaded again when next used.
            if (scope == AccessLogScope.Planet && known is not null && known.IsGoverning != state.IsGoverning)
                MarkPlanetKeyRingsStale(scopeId);

            ScheduleCheckpoint(scope, scopeId, node, state);
            return state;
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            LogError($"Access log {scope}/{scopeId} failed verification", e);
            return null;
        }
    }

    /// <summary>
    /// Checks a log verified from its latest checkpoint against this device's
    /// pin. When the pinned entry comes before the checkpoint, the whole log is
    /// read, one page at a time, and must contain it and lead to the same
    /// state. A log the server does not return up to the checkpoint is not
    /// accepted, because a cut-short copy could hide the entries that make a
    /// forged checkpoint visible.
    /// </summary>
    private async Task<bool> MatchesPinAsync(AccessLogScope scope, long scopeId, Node node,
        List<AccessLogEntry> entries, AccessLogState state, AccessPin pin)
    {
        if (entries[0].Seq <= pin.Count - 1)
            return ExtendsPin(entries, pin);

        var all = await FetchWholeAccessLogAsync(scope, scopeId, node, state.HeadSeq);
        if (all is null || !ExtendsPin(all, pin))
            return false;

        var replayed = AccessLogVerifier.Verify(scope, scopeId, all,
            await GetUserStatesAsync(AccessLogVerifier.RequiredUsers(all)));
        return replayed.HeadSeq == state.HeadSeq &&
               replayed.HeadHash.AsSpan().SequenceEqual(state.HeadHash);
    }

    /// <summary>
    /// Reads a log from its first entry through <paramref name="throughSeq"/>,
    /// one page at a time. Returns null when the server does not return it in
    /// full and in order. Entries after <paramref name="throughSeq"/>, added
    /// since the caller read the log, are left out.
    /// </summary>
    private async Task<List<AccessLogEntry>> FetchWholeAccessLogAsync(AccessLogScope scope, long scopeId, Node node,
        int throughSeq)
    {
        var all = new List<AccessLogEntry>();
        while (all.Count == 0 || all[^1].Seq < throughSeq)
        {
            var page = await node.GetJsonAsync<List<AccessLogEntry>>(
                $"{AccessLogRoute(scope, scopeId)}?known={all.Count}&full=true", cacheDurationMs: null);
            if (!page.Success || page.Data is not { Count: > 0 } received ||
                received.Where((e, i) => e.Seq != all.Count + i).Any())
            {
                LogWarning($"Access log {scope}/{scopeId} could not be read in full");
                return null;
            }

            all.AddRange(received);
        }

        all.RemoveAll(e => e.Seq > throughSeq);
        return all;
    }

    private static bool ExtendsPin(List<AccessLogEntry> entries, AccessPin pin)
    {
        var pinned = entries.FirstOrDefault(e => e.Seq == pin.Count - 1);
        return pinned is not null && Base64Url.Encode(pinned.Hash()) == pin.HeadHash;
    }

    /// <summary>
    /// An owner's or admin's device signs a checkpoint once the log has grown
    /// past <see cref="AccessLogCheckpointInterval"/> entries since the last one.
    /// </summary>
    private void ScheduleCheckpoint(AccessLogScope scope, long scopeId, Node node, AccessLogState state)
    {
        if (Status != E2eeStatus.Ready || MyKeyState is null || !state.IsGoverning ||
            state.HeadSeq - Math.Max(state.LastCheckpointSeq, 0) < AccessLogCheckpointInterval ||
            !state.IsAdmin(_client.Me.Id, MyKeyState) ||
            !_checkpointsInProgress.TryAdd((scope, scopeId), 0))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await AppendAccessLogAsync(scope, scopeId, node, AccessLogEntryType.Checkpoint,
                    (current, record) => record.With(snapshot: AccessLogSnapshot.FromState(current)));
                if (!result.Success)
                    LogWarning($"Could not checkpoint access log {scope}/{scopeId}: {result.Message}");
            }
            finally
            {
                _checkpointsInProgress.TryRemove((scope, scopeId), out _);
            }
        });
    }

    private Task<TaskResult<AccessLogState>> AppendAccessLogAsync(AccessLogScope scope, long scopeId, Node node,
        AccessLogEntryType type, Func<AccessLogRecord, AccessLogRecord> fill) =>
        AppendAccessLogAsync(scope, scopeId, node, type, (_, record) => fill(record));

    private async Task<TaskResult<AccessLogState>> AppendAccessLogAsync(AccessLogScope scope, long scopeId, Node node,
        AccessLogEntryType type, Func<AccessLogState, AccessLogRecord, AccessLogRecord> fill)
    {
        if (Status != E2eeStatus.Ready)
            return TaskResult<AccessLogState>.FromFailure("Verify this device first.");

        // Retry once if another member appended at the same moment.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var existing = await GetAccessLogStateAsync(scope, scopeId, node, refresh: true);
            if (existing is null && await HasAccessPinAsync(scope, scopeId))
                return TaskResult<AccessLogState>.FromFailure(
                    "The membership log could not be verified against the one this device saw before.");

            var state = existing ?? new AccessLogState { Scope = scope, ScopeId = scopeId };
            var entry = AccessLogBuilder.Create(state, type, _client.Me.Id, Device, NowMs(),
                record => fill(state, record));
            var result = await node.PostAsync(AccessLogRoute(scope, scopeId), entry);
            if (result.Success)
            {
                var updated = await GetAccessLogStateAsync(scope, scopeId, node, refresh: true);
                return TaskResult<AccessLogState>.FromData(updated);
            }

            if (!E2eeErrorCodes.Is(result.Message, E2eeErrorCodes.AccessLogChanged))
                return TaskResult<AccessLogState>.FromFailure(result.Message);
        }

        return TaskResult<AccessLogState>.FromFailure("The membership log keeps changing. Try again.");
    }

    /// <summary>
    /// The current keys of the given users. Users who have not set up
    /// encryption are left out: an admission names the keys it trusts, so
    /// nobody is admitted before they have keys. They stay pending until they
    /// set them up and someone admits them.
    /// </summary>
    private async Task<List<AccessMember>> MembersWithKeysAsync(IEnumerable<long> userIds)
    {
        var ids = userIds.Distinct().ToList();
        var states = await GetUserStatesAsync(ids);
        return ids.Where(id => states.TryGetValue(id, out var s) && s?.HasIdentity == true)
            .Select(id => AccessMember.For(states[id]))
            .ToList();
    }

    /// <summary>
    /// Members per <see cref="AccessLogEntryType.AddMembers"/> entry, which
    /// keeps each entry under the server's size limit for membership changes.
    /// </summary>
    private const int MaxMembersPerEntry = 8_000;

    /// <summary>Members in a <see cref="AccessLogEntryType.Genesis"/> or <see cref="AccessLogEntryType.Restart"/> entry.</summary>
    private const int MaxMembersPerStartEntry = 30_000;

    // Group DMs

    /// <summary>
    /// Starts a group's signed membership log if it has none. Any member can
    /// start it, because the group cannot send messages until it exists; the
    /// member who starts it becomes the log's owner.
    /// </summary>
    public async Task<TaskResult> EnsureGroupAccessLogAsync(Channel channel)
    {
        if (await GetAccessLogStateAsync(AccessLogScope.GroupChannel, channel.Id, channel.Node, refresh: true) is not null)
            return TaskResult.SuccessResult;

        var others = channel.Members?.Select(m => m.UserId).Where(id => id != _client.Me.Id) ?? [];
        var members = await MembersWithKeysAsync(others);
        var result = await AppendAccessLogAsync(AccessLogScope.GroupChannel, channel.Id, channel.Node,
            AccessLogEntryType.Genesis,
            r => r.With(owner: AccessMember.For(MyKeyState), members: members));
        return WithoutData(result);
    }

    /// <summary>
    /// Admits people just added to a group DM so members share keys with them.
    /// Does nothing for groups that are not encrypted yet. People who have not
    /// set up encryption are skipped; a member admits them later from the
    /// chat's encryption details.
    /// </summary>
    public async Task<TaskResult> AdmitGroupMembersAsync(Channel channel, IEnumerable<long> userIds)
    {
        if (channel is null || channel.ChannelType != ChannelTypeEnum.GroupChat)
            return TaskResult.SuccessResult;

        var log = await GetAccessLogStateAsync(AccessLogScope.GroupChannel, channel.Id, channel.Node, refresh: true);
        if (log is null)
            return TaskResult.SuccessResult;

        var members = await MembersWithKeysAsync(userIds.Where(id => !log.IsMemberAnyEpoch(id)));
        if (members.Count == 0)
            return TaskResult.SuccessResult;

        var result = await AppendAccessLogAsync(AccessLogScope.GroupChannel, channel.Id, channel.Node,
            AccessLogEntryType.AddMembers, r => r.With(members: members));
        if (!result.Success)
            return Fail(result.Message);

        await ServeChannelAsync(channel);
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Records that someone left or was removed from an encrypted group DM.
    /// The next message rotates the key so they cannot read it.
    /// </summary>
    public async Task<TaskResult> RemoveGroupMemberAsync(Channel channel, long userId)
    {
        if (channel is null || channel.ChannelType != ChannelTypeEnum.GroupChat)
            return TaskResult.SuccessResult;

        var log = await GetAccessLogStateAsync(AccessLogScope.GroupChannel, channel.Id, channel.Node, refresh: true);
        if (log is null || !log.IsMemberAnyEpoch(userId))
            return TaskResult.SuccessResult;

        var result = await AppendAccessLogAsync(AccessLogScope.GroupChannel, channel.Id, channel.Node,
            AccessLogEntryType.RemoveMembers, r => r.With(userIds: [userId]));
        return WithoutData(result);
    }

    // Private planets

    /// <summary>
    /// True when a planet is private but its owner's device has not signed
    /// its membership log yet, so its permissions still decide who receives
    /// keys. The owner finishes this with <see cref="FinishPrivatePlanetAsync"/>.
    /// A planet being moved between servers is never reported as pending.
    /// </summary>
    public static bool IsPrivacyPending(Planet planet) =>
        planet is not null && !planet.Public && planet.EncryptionMode == PlanetEncryptionMode.Open &&
        !planet.LockedForMigration;

    /// <summary>
    /// Whether a planet's channels follow its signed membership log, and the
    /// verified log when this device loaded it. A planet is governed when the
    /// server reports it as invite-only or this device has seen its log,
    /// unless the verified log shows that the owner made it public. A log
    /// that cannot be verified keeps the planet governed, so nobody receives
    /// keys by mistake.
    /// </summary>
    private async Task<(bool Governed, AccessLogState Log)> GetPlanetGovernanceAsync(long planetId, Node node,
        bool reportedInviteOnly)
    {
        if (!reportedInviteOnly && !await HasAccessPinAsync(AccessLogScope.Planet, planetId))
            return (false, null);

        var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planetId, node);

        // Right after the owner changes the planet's privacy, the server's
        // report and this device's copy of the log disagree until the log is
        // loaded again.
        if (log is not null && log.IsOpen == reportedInviteOnly)
            log = await GetAccessLogStateAsync(AccessLogScope.Planet, planetId, node, refresh: true);

        // A planet the server still reports as invite-only stays governed by
        // a log the owner opened. That log admits nobody, so no keys are shared.
        return (log is null || reportedInviteOnly || log.IsGoverning, log);
    }

    /// <summary>
    /// Makes a planet public or private and sets whether new members can read
    /// earlier messages. Only the owner can do this, from a verified device.
    ///
    /// Making a planet private signs its membership log, admitting the
    /// current members who have set up encryption, and replaces keys in the
    /// channels this device can see so later messages reach only admitted
    /// members. Making a private planet public signs an
    /// <see cref="AccessLogEntryType.Open"/> entry; members' apps stop
    /// enforcing the log once they verify it, and keys are not replaced.
    /// </summary>
    public async Task<TaskResult> SetPlanetPrivacyAsync(Planet planet, bool isPublic, bool sharesHistory)
    {
        if (Status != E2eeStatus.Ready)
            return Fail("Verify this device first.");
        if (planet.OwnerId != _client.Me.Id)
            return Fail("Only the planet owner can change whether the planet is public.");

        var existing = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
        if (existing is null && await HasAccessPinAsync(AccessLogScope.Planet, planet.Id))
            return Fail("The planet's membership log could not be verified against the one this device saw before.");
        if (!isPublic && CannotMakePrivateReason(existing) is { } reason)
            return Fail(reason);

        var wasGoverned = existing?.IsGoverning == true;
        var historyChanged = sharesHistory != planet.EncryptionSharesHistory;
        var request = new SetPlanetPrivacyRequest { Public = isPublic, SharesHistory = sharesHistory };
        List<AccessMember> admitAfterStart = [];

        if (!isPublic && !wasGoverned)
        {
            var memberIds = await FetchPlanetMemberIdsAsync(planet);
            if (memberIds is null)
                return Fail("Could not load the planet's members.");

            var members = await MembersWithKeysAsync(memberIds.Where(id => id != _client.Me.Id));

            // A start entry stays under the server's size limit; the rest of
            // the members are admitted right after it.
            admitAfterStart = members.Skip(MaxMembersPerStartEntry).ToList();
            members = members.Take(MaxMembersPerStartEntry).ToList();
            var start = existing ?? new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = planet.Id };
            request.Entry = AccessLogBuilder.Create(start,
                existing is null ? AccessLogEntryType.Genesis : AccessLogEntryType.Restart,
                _client.Me.Id, Device, NowMs(),
                r => r.With(owner: AccessMember.For(MyKeyState), members: members));
        }
        else if (isPublic && wasGoverned)
        {
            if (!existing.IsOwner(_client.Me.Id, MyKeyState))
                return Fail("Your current keys do not own this planet's membership log, so the change cannot be signed.");

            request.Entry = AccessLogBuilder.Create(existing, AccessLogEntryType.Open, _client.Me.Id, Device, NowMs(),
                r => r);
        }

        var result = await planet.Node.PutAsyncWithResponse<Planet>($"api/planets/{planet.Id}/privacy", request);
        if (!result.Success)
            return Fail(result.Message);

        planet.Public = isPublic;
        planet.EncryptionMode = isPublic ? PlanetEncryptionMode.Open : PlanetEncryptionMode.InviteOnly;
        planet.EncryptionSharesHistory = sharesHistory;
        if (!isPublic)
            planet.VanityInviteEnabled = false;
        _accessLogs.TryRemove((AccessLogScope.Planet, planet.Id), out _);
        var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
        if (!isPublic && log?.IsGoverning != true)
            return Fail("The planet is private, but its membership log could not be verified.");

        foreach (var chunk in admitAfterStart.Chunk(MaxMembersPerEntry))
        {
            var admitted = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
                AccessLogEntryType.AddMembers, r => r.With(members: chunk.ToList()));
            if (!admitted.Success)
                return Fail("The planet is private, but some members could not be admitted: " + admitted.Message);
        }

        // A public planet's keys follow its permissions, which already let
        // everyone who holds them read. A planet that just became private, or
        // whose history setting changed, gets new keys.
        if (isPublic || (wasGoverned && !historyChanged))
            return TaskResult.SuccessResult;

        // Replace keys that members who are not admitted might hold. Channels
        // without a key get their first one when someone sends a message.
        var failures = 0;
        foreach (var channel in planet.Channels.Where(c => c.ChannelType == ChannelTypeEnum.PlanetChat))
        {
            var ring = await GetKeyRingAsync(channel, refresh: true);
            if (ring is null)
            {
                failures++;
                continue;
            }

            // The owner's explicit history setting goes into the new keys;
            // other rotations carry the signed setting forward.
            if (ring.LatestGeneration > 0 && ring.Latest is not null &&
                !(await RotateChannelKeyAsync(channel, ChannelKeyRotationReason.MemberRemoved, rotateIndex: false,
                    sharesHistory: sharesHistory)).Success)
                failures++;
        }

        return failures == 0
            ? TaskResult.SuccessResult
            : Fail($"The planet is private, but {failures} channel(s) could not get new keys yet. They are replaced when a member with access sends a message.");
    }

    /// <summary>
    /// Shown when the owner's keys were started over after the planet was
    /// made public. The opened log only accepts a change from the keys that
    /// owned it, so the planet cannot become private again under that log.
    /// </summary>
    public const string OpenedLogOwnerKeysChangedMessage =
        "Your encryption was started over after this planet became public. It can stay public, but it can't be " +
        "made private again, because only the keys you had then can sign that change.";

    /// <summary>
    /// Shown to a planet owner whose planet was handed over while public by
    /// an owner who could no longer sign for its membership log.
    /// </summary>
    public const string OpenedLogOwnedByEarlierKeysMessage =
        "This planet can stay public, but it can't be made private again. Its membership log still belongs to " +
        "an earlier owner's keys, which were started over, and only those keys can sign that change.";

    /// <summary>
    /// Why this account cannot make a public planet private again, or null
    /// when it can. A log the owner opened only accepts a restart from the
    /// keys it names as the owner's.
    /// </summary>
    public async Task<string> GetMakePrivateBlockerAsync(Planet planet)
    {
        if (Status != E2eeStatus.Ready || planet.OwnerId != _client.Me.Id)
            return null;
        return CannotMakePrivateReason(
            await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node));
    }

    private string CannotMakePrivateReason(AccessLogState log)
    {
        if (log is not { IsOpen: true } || log.IsOwner(_client.Me.Id, MyKeyState))
            return null;

        return log.Owner.UserId == _client.Me.Id
            ? OpenedLogOwnerKeysChangedMessage
            : OpenedLogOwnedByEarlierKeysMessage;
    }

    /// <summary>
    /// Signs the membership log of a private planet whose owner has not done
    /// so yet (see <see cref="IsPrivacyPending"/>), admitting its current
    /// members who have set up encryption.
    /// </summary>
    public Task<TaskResult> FinishPrivatePlanetAsync(Planet planet) =>
        SetPlanetPrivacyAsync(planet, isPublic: false, planet.EncryptionSharesHistory);

    private async Task<List<long>> FetchPlanetMemberIdsAsync(Planet planet)
    {
        var result = await planet.Node.GetJsonAsync<List<long>>($"api/e2ee/planets/{planet.Id}/member-ids",
            cacheDurationMs: null);
        return result.Success ? result.Data : null;
    }

    /// <summary>
    /// Members of a private planet waiting to be admitted, and admitted
    /// members whose keys changed and need confirming.
    /// </summary>
    public async Task<List<PendingAdmission>> GetPendingAdmissionsAsync(Planet planet)
    {
        var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
        var memberIds = await FetchPlanetMemberIdsAsync(planet);
        if (log is null || memberIds is null)
            return [];

        var states = await GetUserStatesAsync(memberIds);
        var pending = new List<PendingAdmission>();
        foreach (var userId in memberIds)
        {
            states.TryGetValue(userId, out var state);
            var keys = state?.HasIdentity == true ? state : null;
            if (!log.IsMemberAnyEpoch(userId))
            {
                pending.Add(new PendingAdmission
                {
                    UserId = userId, Epoch = keys?.Epoch ?? 0, SafetyNumber = keys?.SafetyNumber()
                });
            }
            else if (keys is not null && !log.IsMember(userId, keys))
            {
                pending.Add(new PendingAdmission
                {
                    UserId = userId, KeysChanged = true, Epoch = keys.Epoch, SafetyNumber = keys.SafetyNumber()
                });
            }
        }

        return pending;
    }

    /// <summary>
    /// People the membership log still admits who are no longer in the
    /// planet, for example after a moderator without admission rights kicked
    /// them. Admins should remove them so rejoining does not restore key access.
    /// </summary>
    public async Task<List<long>> GetStaleAdmissionsAsync(Planet planet)
    {
        var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
        var memberIds = await FetchPlanetMemberIdsAsync(planet);
        if (log is null || memberIds is null)
            return [];

        var members = memberIds.ToHashSet();
        return log.Members.Keys.Where(id => id != log.Owner.UserId && !members.Contains(id)).ToList();
    }

    /// <summary>
    /// Admits members to a private planet with their current keys.
    /// People who have not set up encryption are skipped and stay pending.
    /// </summary>
    public async Task<TaskResult> AdmitPlanetMembersAsync(Planet planet, IEnumerable<long> userIds)
    {
        var ids = userIds.Distinct().ToList();
        var members = await MembersWithKeysAsync(ids);
        var skipped = ids.Count - members.Count;
        if (members.Count == 0)
            return ids.Count == 0
                ? TaskResult.SuccessResult
                : Fail("They haven't set up encryption yet. Let them in after they sign in and set it up.");

        foreach (var chunk in members.Chunk(MaxMembersPerEntry))
        {
            var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
                AccessLogEntryType.AddMembers, r => r.With(members: chunk.ToList()));
            if (!result.Success)
                return Fail(result.Message);
        }

        return skipped == 0
            ? TaskResult.SuccessResult
            : TaskResult.FromSuccess($"Let {members.Count} in. {skipped} haven't set up encryption yet and are still waiting.");
    }

    /// <summary>
    /// Removes people from a private planet's membership log, for example
    /// after a kick or ban. The next message in each channel rotates its key.
    /// </summary>
    public async Task<TaskResult> RemovePlanetMembersAsync(Planet planet, IEnumerable<long> userIds)
    {
        if (planet.EncryptionMode != PlanetEncryptionMode.InviteOnly)
            return TaskResult.SuccessResult;

        var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
        var ids = userIds.Where(id => log?.IsMemberAnyEpoch(id) == true && id != log.Owner.UserId).ToList();
        if (ids.Count == 0)
            return TaskResult.SuccessResult;

        var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            AccessLogEntryType.RemoveMembers, r => r.With(userIds: ids));
        return WithoutData(result);
    }

    /// <summary>
    /// Confirms a member's new keys after they reset them. Only confirm after
    /// checking with the person, for example by comparing security numbers.
    /// Pass the number that was compared; if the member's keys changed since
    /// it was shown, nothing is confirmed and the call fails.
    /// </summary>
    public async Task<TaskResult> ConfirmMemberKeysAsync(AccessLogScope scope, long scopeId, Node node, long userId,
        string expectedSafetyNumber)
    {
        var state = await GetUserStateAsync(userId, refresh: true);
        if (state?.HasIdentity != true)
            return Fail("That person has not set up encryption.");
        if (!MatchesSafetyNumber(state, expectedSafetyNumber))
            return Fail(KeysChangedMessage);

        var result = await AppendAccessLogAsync(scope, scopeId, node, AccessLogEntryType.ConfirmEpoch,
            r => r.With(target: AccessMember.For(state)));
        return WithoutData(result);
    }

    /// <summary>
    /// Makes an admitted member an admin of a private planet's membership
    /// log, or removes them as one. Only the log's owner can do this. A member
    /// whose keys changed since they were admitted needs confirming first.
    /// </summary>
    public async Task<TaskResult> SetPlanetAccessAdminAsync(Planet planet, long userId, bool isAdmin)
    {
        AccessMember target;
        if (isAdmin)
        {
            var state = await GetUserStateAsync(userId, refresh: true);
            var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
            if (state?.HasIdentity != true || log is null || !log.IsMember(userId, state))
                return Fail("Only admitted members whose keys are confirmed can become admins.");
            target = AccessMember.For(state);
        }
        else
        {
            // Removing an admin names only the user.
            target = new AccessMember(userId, 0, new byte[UserKeyState.KeyIdSize]);
        }

        var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            AccessLogEntryType.SetAdmin, r => r.With(target: target, isAdmin: isAdmin));
        return WithoutData(result);
    }

    /// <summary>
    /// Creates a signed invite for a private planet. Append
    /// <see cref="EncryptedInvite.Fragment"/> to the invite link. Pass
    /// <paramref name="inviteId"/> to name the invite after the planet invite
    /// code it belongs to, so deleting that code can revoke it.
    /// </summary>
    public async Task<TaskResult<EncryptedInvite>> CreatePlanetInviteAsync(Planet planet, DateTime? expires, int maxUses,
        string inviteId = null)
    {
        var secret = E2eeCrypto.RandomBytes(16);
        var (_, publicKey) = AccessLogBuilder.InviteKey(secret);
        inviteId ??= Base64Url.Encode(E2eeCrypto.RandomBytes(9));
        var expiresMs = expires is null ? 0 : new DateTimeOffset(expires.Value.ToUniversalTime()).ToUnixTimeMilliseconds();

        var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            AccessLogEntryType.CreateInvite,
            r => r.With(inviteId: inviteId, invitePublicKey: publicKey, inviteExpiresMs: expiresMs,
                inviteMaxUses: Math.Max(0, maxUses)));
        if (!result.Success)
            return TaskResult<EncryptedInvite>.FromFailure(result.Message);

        return TaskResult<EncryptedInvite>.FromData(new EncryptedInvite { InviteId = inviteId, Secret = secret });
    }

    public async Task<TaskResult> RevokePlanetInviteAsync(Planet planet, string inviteId)
    {
        var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            AccessLogEntryType.RevokeInvite, r => r.With(inviteId: inviteId));
        if (result.Success)
            await ForgetInviteSecretAsync(planet.Id, inviteId);
        return WithoutData(result);
    }

    /// <summary>
    /// True when this device can sign the planet's invite links so that
    /// people who use them are let in right away: the planet is private and
    /// this account is an admin of its membership log on a verified device.
    /// </summary>
    public async Task<bool> CanSignPlanetInvitesAsync(Planet planet) =>
        planet.EncryptionMode == PlanetEncryptionMode.InviteOnly && await IsPlanetAccessAdminAsync(planet);

    /// <summary>
    /// Signs a planet invite code into a private planet's membership log, so
    /// whoever joins with the link is let in without waiting for an admin.
    /// The signed invite is named after the code and expires with it. Server
    /// invite codes have no use limit, so neither does the signed invite.
    /// Append <see cref="EncryptedInvite.Fragment"/> to the invite link; the
    /// secret it carries is never sent to the server.
    /// </summary>
    public async Task<TaskResult<EncryptedInvite>> SignPlanetInviteAsync(Planet planet, PlanetInvite invite)
    {
        if (invite?.Id is null || invite.PlanetId != planet.Id)
            return TaskResult<EncryptedInvite>.FromFailure("The invite does not belong to this planet.");
        if (!await CanSignPlanetInvitesAsync(planet))
            return TaskResult<EncryptedInvite>.FromFailure(
                "Only an admin of this private planet, on a verified device, can sign its invite links.");

        // Server times are UTC even when the JSON carries no zone.
        DateTime? expires = invite.TimeExpires is { } time
            ? time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time
            : null;
        var signed = await CreatePlanetInviteAsync(planet, expires, maxUses: 0, inviteId: invite.Id);

        // This device keeps the secret so the full link can be copied again.
        if (signed.Success)
            await SaveInviteSecretAsync(planet.Id, signed.Data, expires);
        return signed;
    }

    /// <summary>
    /// Deletes a planet invite code. When the code was signed into a private
    /// planet's membership log, its signed invite is revoked first, so the
    /// secret in the link cannot let anyone in through another code. Only an
    /// admin of the log, on a verified device, can revoke it, so nobody else
    /// deletes such a code; it stops working when it expires. A secret this
    /// device saved for the code is forgotten.
    /// </summary>
    public async Task<TaskResult> DeletePlanetInviteAsync(Planet planet, PlanetInvite invite)
    {
        if (planet.EncryptionMode == PlanetEncryptionMode.InviteOnly)
        {
            var log = Status == E2eeStatus.Ready
                ? await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true)
                : null;
            var signed = log is { IsGoverning: true } && log.Invites.TryGetValue(invite.Id, out var entry) &&
                         !entry.Revoked;
            if (signed)
            {
                if (!log.IsAdmin(_client.Me.Id, MyKeyState))
                    return Fail("This link lets people in right away, so only an admin of this private planet, " +
                                "on a verified device, can delete it.");

                var revoked = await RevokePlanetInviteAsync(planet, invite.Id);
                if (!revoked.Success)
                    return revoked;
            }
            else if (log is null && Status != E2eeStatus.Ready)
            {
                return Fail("Verify this device first, so it can check whether this link lets people in right away.");
            }
        }

        var deleted = await invite.DeleteAsync();
        if (deleted.Success)
            await ForgetInviteSecretAsync(planet.Id, invite.Id);
        return deleted;
    }

    /// <summary>
    /// Uses a signed invite after joining a private planet, admitting
    /// this account without waiting for an admin. A planet that became public
    /// since the invite was made lets everyone read, so there is nothing to
    /// redeem.
    /// </summary>
    public async Task<TaskResult> RedeemPlanetInviteAsync(Planet planet, EncryptedInvite invite)
    {
        if (Status != E2eeStatus.Ready)
            return Fail("Verify this device first.");

        var (governed, _) = await GetPlanetGovernanceAsync(planet.Id, planet.Node,
            planet.EncryptionMode == PlanetEncryptionMode.InviteOnly);
        if (!governed)
            return TaskResult.SuccessResult;

        var (seed, _) = AccessLogBuilder.InviteKey(invite.Secret);
        var proof = E2eeCrypto.Sign(seed, AccessLogRecord.InviteProofMessage(AccessLogScope.Planet, planet.Id,
            invite.InviteId, _client.Me.Id, Device.DeviceId));

        var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            AccessLogEntryType.RedeemInvite,
            r => r.With(inviteId: invite.InviteId, target: AccessMember.For(MyKeyState), inviteProof: proof));
        return WithoutData(result);
    }

    /// <summary>
    /// Removes this account from a private planet's membership log before
    /// leaving it.
    /// </summary>
    public async Task<TaskResult> LeavePlanetAccessAsync(Planet planet)
    {
        if (planet.EncryptionMode != PlanetEncryptionMode.InviteOnly || Status != E2eeStatus.Ready)
            return TaskResult.SuccessResult;

        var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
        if (log is null || !log.IsMemberAnyEpoch(_client.Me.Id) || log.Owner.UserId == _client.Me.Id)
            return TaskResult.SuccessResult;

        var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            AccessLogEntryType.RemoveMembers, r => r.With(userIds: [_client.Me.Id]));
        return WithoutData(result);
    }

    /// <summary>
    /// True when this account can admit members to a private planet.
    /// </summary>
    public async Task<bool> IsPlanetAccessAdminAsync(Planet planet)
    {
        if (Status != E2eeStatus.Ready)
            return false;
        var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node);
        return log?.IsGoverning == true && log.IsAdmin(_client.Me.Id, MyKeyState);
    }

    /// <summary>
    /// Transfers a planet's ownership. A planet's membership log names its
    /// own owner, and only the owner's device can change that, so this device
    /// signs a <see cref="AccessLogEntryType.TransferOwnership"/> entry naming
    /// the new owner's current keys and sends it with the request. The server
    /// appends it in the same transaction that changes the planet's owner,
    /// and refuses to transfer a planet that has a log without it unless the
    /// log already names the new owner. This includes a public planet whose
    /// log the owner opened, since only the log's owner can make the planet
    /// private again. The one exception is a public planet whose opened log
    /// names keys this account no longer holds: it moves without an entry,
    /// and nobody can make it private again under that log.
    /// </summary>
    public async Task<TaskResult<Planet>> TransferPlanetOwnershipAsync(Planet planet, long newOwnerUserId,
        string multiFactorCode)
    {
        var request = new PlanetOwnershipTransferRequest
        {
            NewOwnerUserId = newOwnerUserId,
            MultiFactorCode = multiFactorCode
        };

        // A public planet may still have a log its owner opened, so a device
        // with keys looks the log up whatever the planet reports.
        var log = Status == E2eeStatus.Ready
            ? await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true)
            : null;
        var hasLog = log is not null || planet.EncryptionMode == PlanetEncryptionMode.InviteOnly ||
                     await HasAccessPinAsync(AccessLogScope.Planet, planet.Id);
        if (hasLog)
        {
            if (Status != E2eeStatus.Ready)
                return TaskResult<Planet>.FromFailure(
                    "This planet has a membership log. Verify this device first, so it can sign the change to it.");
            if (log is null)
                return TaskResult<Planet>.FromFailure("The planet's membership log could not be verified.");

            // A public planet whose log names keys this account no longer
            // holds moves without an entry; the server checks this itself,
            // and the log stays with the keys it names.
            var canSign = log.IsOwner(_client.Me.Id, MyKeyState);
            if (log.Owner.UserId != newOwnerUserId && (canSign || !log.IsOpen))
            {
                if (!canSign)
                    return TaskResult<Planet>.FromFailure(
                        "Your current keys do not own this planet's membership log, so the transfer cannot be signed.");

                var newOwner = await GetUserStateAsync(newOwnerUserId, refresh: true);
                if (newOwner?.HasIdentity != true)
                    return TaskResult<Planet>.FromFailure(
                        "The new owner needs to sign in to Valour once, so their device sets up encryption, first.");
                if (log.IsGoverning && !log.IsMember(newOwnerUserId, newOwner))
                    return TaskResult<Planet>.FromFailure(
                        "The new owner must be let in to the private planet, with confirmed keys, first.");

                var entry = AccessLogBuilder.Create(log, AccessLogEntryType.TransferOwnership, _client.Me.Id, Device,
                    NowMs(), r => r.With(target: AccessMember.For(newOwner)));
                request.AccessLogEntryBody = entry.Body;
                request.AccessLogEntrySignature = entry.Signature;
            }
        }

        var result = await planet.Node.PostAsyncWithResponse<Planet>(
            $"api/planets/{planet.Id}/transfer-ownership", request);
        if (result.Success && hasLog)
            await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
        return result;
    }
}
