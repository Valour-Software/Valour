using System.Collections.Concurrent;
using System.Text.Json;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

/// <summary>
/// An invite for an invite-only planet whose secret travels in the link's
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
/// A member of an invite-only planet or encrypted group who is waiting on an
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
                await Store.SetAsync(AccessStateKey(state.Scope, state.ScopeId), state.Encode());
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

    private Task<Dictionary<string, AccessPin>> LoadAccessPinsAsync() =>
        LoadStoredJsonAsync<Dictionary<string, AccessPin>>(AccessPinsKey);

    private Task SaveAccessPinsAsync() =>
        Store.SetAsync(StoreKey(AccessPinsKey), JsonSerializer.SerializeToUtf8Bytes(_accessPins));

    private readonly ConcurrentDictionary<(AccessLogScope, long), AccessLogState> _accessLogs = new();
    private readonly ConcurrentDictionary<(AccessLogScope, long), byte> _checkpointsInProgress = new();

    private static string AccessLogRoute(AccessLogScope scope, long scopeId) =>
        $"api/e2ee/access-logs/{(int)scope}/{scopeId}";

    /// <summary>
    /// Returns the verified access log for an invite-only planet or group DM,
    /// or null when it has none.
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
                state = AccessLogVerifier.Verify(scope, scopeId, entries, states);
                if (pin is not null && !await MatchesPinAsync(scope, scopeId, node, entries, state, pin))
                {
                    LogError($"Access log {scope}/{scopeId} conflicts with the one this device verified before");
                    return null;
                }
            }

            await SaveAccessLogAsync(state);
            _accessLogs[(scope, scopeId)] = state;
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
    /// read and must contain it and lead to the same state. A log too large to
    /// return whole is accepted from its checkpoint, as a new device would.
    /// </summary>
    private async Task<bool> MatchesPinAsync(AccessLogScope scope, long scopeId, Node node,
        List<AccessLogEntry> entries, AccessLogState state, AccessPin pin)
    {
        if (entries[0].Seq <= pin.Count - 1)
            return ExtendsPin(entries, pin);

        var full = await node.GetJsonAsync<List<AccessLogEntry>>(
            $"{AccessLogRoute(scope, scopeId)}?known=0&full=true", cacheDurationMs: null);
        if (!full.Success || full.Data is not { Count: > 0 } all || all[0].Seq != 0)
            return false;

        if (all[^1].Seq < state.HeadSeq)
        {
            LogWarning($"Access log {scope}/{scopeId} is too large to check against this device's earlier copy, " +
                       "so it is trusted from its latest checkpoint");
            return true;
        }

        if (!ExtendsPin(all, pin))
            return false;

        var replayed = AccessLogVerifier.Verify(scope, scopeId, all,
            await GetUserStatesAsync(AccessLogVerifier.RequiredUsers(all)));
        return replayed.HeadSeq == state.HeadSeq &&
               replayed.HeadHash.AsSpan().SequenceEqual(state.HeadHash);
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
        if (Status != E2eeStatus.Ready || MyKeyState is null ||
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

    // Invite-only planets

    /// <summary>
    /// Changes who receives a planet's keys and whether new members can read
    /// earlier messages. Only the owner can do this. Making a planet
    /// invite-only signs its membership log and replaces keys in the channels
    /// this device can see, so members who were not admitted lose access to
    /// later messages.
    /// </summary>
    public async Task<TaskResult> SetPlanetEncryptionAsync(Planet planet, PlanetEncryptionMode mode, bool sharesHistory,
        bool restartMembership = false)
    {
        if (Status != E2eeStatus.Ready)
            return Fail("Verify this device first.");
        if (planet.OwnerId != _client.Me.Id)
            return Fail("Only the planet owner can change encryption.");

        var request = new SetPlanetEncryptionRequest { Mode = mode, SharesHistory = sharesHistory };
        List<AccessMember> admitAfterStart = [];

        if (mode == PlanetEncryptionMode.InviteOnly)
        {
            var existing = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
            if (existing is null || restartMembership)
            {
                var memberIds = await FetchPlanetMemberIdsAsync(planet);
                if (memberIds is null)
                    return Fail("Could not load the planet's members.");

                var members = await MembersWithKeysAsync(memberIds.Where(id => id != _client.Me.Id));

                // A start entry stays under the server's size limit; the rest
                // of the members are admitted right after it.
                admitAfterStart = members.Skip(MaxMembersPerStartEntry).ToList();
                members = members.Take(MaxMembersPerStartEntry).ToList();
                var start = existing ?? new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = planet.Id };
                request.Genesis = AccessLogBuilder.Create(start,
                    existing is null ? AccessLogEntryType.Genesis : AccessLogEntryType.Restart,
                    _client.Me.Id, Device, NowMs(),
                    r => r.With(owner: AccessMember.For(MyKeyState), members: members));
            }
        }

        var result = await planet.Node.PutAsyncWithResponse<Planet>($"api/planets/{planet.Id}/encryption", request);
        if (!result.Success)
            return Fail(result.Message);

        planet.EncryptionMode = mode;
        planet.EncryptionSharesHistory = sharesHistory;
        _accessLogs.TryRemove((AccessLogScope.Planet, planet.Id), out _);
        if (mode == PlanetEncryptionMode.InviteOnly &&
            await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true) is null)
            return Fail("The planet is invite-only, but its membership log could not be verified.");

        foreach (var chunk in admitAfterStart.Chunk(MaxMembersPerEntry))
        {
            var admitted = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
                AccessLogEntryType.AddMembers, r => r.With(members: chunk.ToList()));
            if (!admitted.Success)
                return Fail("The planet is invite-only, but some members could not be admitted: " + admitted.Message);
        }

        if (mode != PlanetEncryptionMode.InviteOnly)
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
            : Fail($"The planet is invite-only, but {failures} channel(s) could not get new keys yet. They are replaced when a member with access sends a message.");
    }

    private async Task<List<long>> FetchPlanetMemberIdsAsync(Planet planet)
    {
        var result = await planet.Node.GetJsonAsync<List<long>>($"api/e2ee/planets/{planet.Id}/member-ids",
            cacheDurationMs: null);
        return result.Success ? result.Data : null;
    }

    /// <summary>
    /// Members of an invite-only planet waiting to be admitted, and admitted
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
    /// Admits members to an invite-only planet with their current keys.
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
                : Fail("They have not set up encryption yet. Admit them after they sign in.");

        foreach (var chunk in members.Chunk(MaxMembersPerEntry))
        {
            var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
                AccessLogEntryType.AddMembers, r => r.With(members: chunk.ToList()));
            if (!result.Success)
                return Fail(result.Message);
        }

        return skipped == 0
            ? TaskResult.SuccessResult
            : TaskResult.FromSuccess($"Admitted {members.Count}. {skipped} have not set up encryption yet and stay waiting.");
    }

    /// <summary>
    /// Removes people from an invite-only planet's membership log, for example
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
    /// Makes an admitted member an admin of an invite-only planet's membership
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
    /// Creates a signed invite for an invite-only planet. Append
    /// <see cref="EncryptedInvite.Fragment"/> to the invite link.
    /// </summary>
    public async Task<TaskResult<EncryptedInvite>> CreatePlanetInviteAsync(Planet planet, DateTime? expires, int maxUses)
    {
        var secret = E2eeCrypto.RandomBytes(16);
        var (_, publicKey) = AccessLogBuilder.InviteKey(secret);
        var inviteId = Base64Url.Encode(E2eeCrypto.RandomBytes(9));
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
        return WithoutData(result);
    }

    /// <summary>
    /// Uses a signed invite after joining an invite-only planet, admitting
    /// this account without waiting for an admin.
    /// </summary>
    public async Task<TaskResult> RedeemPlanetInviteAsync(Planet planet, EncryptedInvite invite)
    {
        if (Status != E2eeStatus.Ready)
            return Fail("Verify this device first.");

        var (seed, _) = AccessLogBuilder.InviteKey(invite.Secret);
        var proof = E2eeCrypto.Sign(seed, AccessLogRecord.InviteProofMessage(AccessLogScope.Planet, planet.Id,
            invite.InviteId, _client.Me.Id, Device.DeviceId));

        var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            AccessLogEntryType.RedeemInvite,
            r => r.With(inviteId: invite.InviteId, target: AccessMember.For(MyKeyState), inviteProof: proof));
        return WithoutData(result);
    }

    /// <summary>
    /// Removes this account from an invite-only planet's membership log
    /// before leaving it.
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
    /// True when this account can admit members to an invite-only planet.
    /// </summary>
    public async Task<bool> IsPlanetAccessAdminAsync(Planet planet)
    {
        if (Status != E2eeStatus.Ready)
            return false;
        var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node);
        return log?.IsAdmin(_client.Me.Id, MyKeyState) == true;
    }

    /// <summary>
    /// Transfers a planet's ownership. An invite-only planet's membership log
    /// names its own owner, and only the owner's device can change that, so
    /// this device signs a <see cref="AccessLogEntryType.TransferOwnership"/>
    /// entry naming the new owner's current keys and sends it with the
    /// request. The server appends it in the same transaction that changes
    /// the planet's owner, and refuses to transfer an invite-only planet
    /// without it unless the log already names the new owner.
    /// </summary>
    public async Task<TaskResult<Planet>> TransferPlanetOwnershipAsync(Planet planet, long newOwnerUserId,
        string multiFactorCode)
    {
        var request = new PlanetOwnershipTransferRequest
        {
            NewOwnerUserId = newOwnerUserId,
            MultiFactorCode = multiFactorCode
        };

        var governed = planet.EncryptionMode == PlanetEncryptionMode.InviteOnly ||
                       await HasAccessPinAsync(AccessLogScope.Planet, planet.Id);
        if (governed)
        {
            if (Status != E2eeStatus.Ready)
                return TaskResult<Planet>.FromFailure(
                    "This planet is invite-only. Verify this device first, so it can sign the change to the membership log.");

            var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
            if (log is null)
                return TaskResult<Planet>.FromFailure("The planet's membership log could not be verified.");

            if (log.Owner.UserId != newOwnerUserId)
            {
                if (!log.IsOwner(_client.Me.Id, MyKeyState))
                    return TaskResult<Planet>.FromFailure(
                        "Your current keys do not own this planet's membership log, so the transfer cannot be signed.");

                var newOwner = await GetUserStateAsync(newOwnerUserId, refresh: true);
                if (newOwner?.HasIdentity != true || !log.IsMember(newOwnerUserId, newOwner))
                    return TaskResult<Planet>.FromFailure(
                        "The new owner must be admitted to the planet's encryption, with confirmed keys, first.");

                var entry = AccessLogBuilder.Create(log, AccessLogEntryType.TransferOwnership, _client.Me.Id, Device,
                    NowMs(), r => r.With(target: AccessMember.For(newOwner)));
                request.AccessLogEntryBody = entry.Body;
                request.AccessLogEntrySignature = entry.Signature;
            }
        }

        var result = await planet.Node.PostAsyncWithResponse<Planet>(
            $"api/planets/{planet.Id}/transfer-ownership", request);
        if (result.Success && governed)
            await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
        return result;
    }
}
