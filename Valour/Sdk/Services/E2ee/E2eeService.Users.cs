using System.Collections.Concurrent;
using System.Text.Json;
using Valour.Sdk.E2ee;
using Valour.Shared;

namespace Valour.Sdk.Services;

public partial class E2eeService
{
    private static readonly TimeSpan UserStateLifetime = TimeSpan.FromMinutes(5);

    private sealed class UserEntry
    {
        public List<UserKeyLogEntry> Entries { get; init; }
        public UserKeyState State { get; init; }
        public DateTime FetchedAt { get; init; }
    }

    /// <summary>
    /// What this device last saw of a user's key log. A later log must extend
    /// it; anything else means the server showed a conflicting history.
    /// </summary>
    internal sealed class KeyPin
    {
        public int Epoch { get; set; }
        public int Count { get; set; }
        public string HeadHash { get; set; }

        /// <summary>
        /// True when the person compared security numbers with this user at
        /// <see cref="Epoch"/>. A reset clears it.
        /// </summary>
        public bool Verified { get; set; }

        /// <summary>
        /// When <see cref="Verified"/> last changed, in Unix milliseconds. Tabs
        /// and processes sharing the store keep the most recent change, so
        /// removing a verification sticks.
        /// </summary>
        public long VerifiedChangedAt { get; set; }

        /// <summary>
        /// The key epoch the person accepted for this user. It starts at the
        /// epoch first seen. When the user resets their keys, direct chats
        /// neither use nor share keys with the new epoch until the person
        /// accepts it, because a reset is also how the server would slip in
        /// keys of its own.
        /// </summary>
        public int? AcceptedEpoch { get; set; }

        /// <summary>
        /// When the person last accepted an epoch, in Unix milliseconds, or 0
        /// for the epoch first seen.
        /// </summary>
        public long AcceptedChangedAt { get; set; }
    }

    private readonly ConcurrentDictionary<long, UserEntry> _userStates = new();
    private readonly SemaphoreSlim _pinLock = new(1, 1);
    private Dictionary<long, KeyPin> _pins;
    private DateTime _pinsReloadedAt;
    private static readonly TimeSpan PinReloadInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Returns a user's verified key state, or null if they have not set up
    /// encryption. Throws <see cref="E2eeVerificationException"/> if the server
    /// shows a key history that conflicts with one this device saw before.
    /// </summary>
    public async Task<UserKeyState> GetUserStateAsync(long userId, bool refresh = false)
    {
        if (_client.Me is not null && userId == _client.Me.Id && MyKeyState is not null && !refresh)
            return MyKeyState;

        if (!refresh && _userStates.TryGetValue(userId, out var cached) &&
            DateTime.UtcNow - cached.FetchedAt < UserStateLifetime)
            return cached.State;

        return (await FetchUserStateAsync(userId)).State;
    }

    /// <summary>
    /// Loads a user's key log, reporting whether the request failed. A user
    /// without keys has a null state and no failure.
    /// </summary>
    private async Task<(UserKeyState State, bool Failed)> FetchUserStateAsync(long userId)
    {
        var known = _userStates.TryGetValue(userId, out var previous) ? previous.Entries.Count : 0;
        var result = await HubNode.GetJsonAsync<List<UserKeyLogEntry>>(
            $"api/e2ee/users/{userId}/log?known={known}", cacheDurationMs: null);
        if (!result.Success)
            return (previous?.State, true);

        var entries = (previous?.Entries ?? []).Concat(result.Data ?? []).ToList();
        return (await AcceptUserLogAsync(userId, entries), false);
    }

    /// <summary>
    /// Fetches several users' key states in one request.
    /// </summary>
    public async Task<Dictionary<long, UserKeyState>> GetUserStatesAsync(IEnumerable<long> userIds)
    {
        var states = new Dictionary<long, UserKeyState>();
        var request = new UserKeyLogsRequest();

        foreach (var userId in userIds.Distinct())
        {
            if (_client.Me is not null && userId == _client.Me.Id && MyKeyState is not null)
            {
                states[userId] = MyKeyState;
                continue;
            }

            if (_userStates.TryGetValue(userId, out var cached) && DateTime.UtcNow - cached.FetchedAt < UserStateLifetime)
            {
                states[userId] = cached.State;
                continue;
            }

            request.KnownCounts[userId] = cached?.Entries.Count ?? 0;
        }

        // The server answers a limited number of users per request. Pins are saved once
        // per batch, because a new key may be sealed to hundreds of members.
        foreach (var chunk in request.KnownCounts.Chunk(E2eeLimits.MaxUsersPerKeyLogRequest))
        {
            var result = await HubNode.PostAsyncWithResponse<Dictionary<long, List<UserKeyLogEntry>>>(
                "api/e2ee/users/logs", new UserKeyLogsRequest { KnownCounts = chunk.ToDictionary() });
            if (!result.Success || result.Data is null)
                continue;

            foreach (var (userId, newEntries) in result.Data)
            {
                var previous = _userStates.TryGetValue(userId, out var p) ? p.Entries : [];
                var entries = previous.Concat(newEntries ?? []).ToList();
                try
                {
                    var state = await AcceptUserLogAsync(userId, entries, savePins: false);
                    if (state is not null)
                        states[userId] = state;
                }
                catch (E2eeVerificationException e)
                {
                    LogError($"Keys for user {userId} failed verification", e);
                }
            }

            await SavePinsLockedAsync();
        }

        return states;
    }

    private async Task<UserKeyState> AcceptUserLogAsync(long userId, List<UserKeyLogEntry> entries,
        bool savePins = true)
    {
        if (entries.Count == 0)
            return null;

        var state = UserKeyLogVerifier.Verify(userId, entries);
        await PinAsync(state, entries, savePins);
        _userStates[userId] = new UserEntry { Entries = entries, State = state, FetchedAt = DateTime.UtcNow };
        return state;
    }

    private async Task PinAsync(UserKeyState state, List<UserKeyLogEntry> entries, bool save = true)
    {
        E2eeContactWarning warning = null;
        await _pinLock.WaitAsync();
        try
        {
            _pins ??= await LoadPinsAsync();
            var head = Base64Url.Encode(entries[^1].Hash());

            // Before trusting a user on first use, check whether another tab
            // or process sharing the store already pinned them. Otherwise a
            // long-lived tab could accept keys another tab saw replaced. The
            // store is read at most once a second, since a batch of new users
            // would otherwise read it once per user.
            if (!_pins.ContainsKey(state.UserId) && DateTime.UtcNow - _pinsReloadedAt > PinReloadInterval)
            {
                _pinsReloadedAt = DateTime.UtcNow;
                foreach (var (userId, stored) in await LoadPinsAsync())
                    _pins.TryAdd(userId, stored);
            }

            if (_pins.TryGetValue(state.UserId, out var pin))
            {
                var extendsPin = entries.Count >= pin.Count &&
                                 Base64Url.Encode(entries[pin.Count - 1].Hash()) == pin.HeadHash;
                if (!extendsPin)
                {
                    warning = new E2eeContactWarning { UserId = state.UserId, Conflict = true };
                    throw new E2eeVerificationException(
                        "This person's security code doesn't match what this device saw before.");
                }

                if (state.Epoch > pin.Epoch)
                {
                    pin.Verified = false;
                    pin.VerifiedChangedAt = NowMs();
                    if (state.UserId != _client.Me?.Id)
                        warning = new E2eeContactWarning { UserId = state.UserId, KeysReset = true };
                }

                if (pin.Count == entries.Count)
                    return;
            }

            _pins[state.UserId] = new KeyPin
            {
                Epoch = state.Epoch,
                Count = entries.Count,
                HeadHash = head,
                Verified = pin?.Verified ?? false,
                VerifiedChangedAt = pin?.VerifiedChangedAt ?? 0,
                AcceptedEpoch = pin is null ? state.Epoch : pin.AcceptedEpoch ?? pin.Epoch,
                AcceptedChangedAt = pin?.AcceptedChangedAt ?? 0
            };
            if (save)
                await SavePinsAsync();
        }
        finally
        {
            _pinLock.Release();

            // Handlers run after the lock is released.
            if (warning is not null)
                ContactWarning?.Invoke(warning);
        }
    }

    /// <summary>
    /// True when the user reset their keys since the person last accepted
    /// them. Direct chats with them pause until the reset is accepted.
    /// </summary>
    public async Task<bool> IsKeyResetPendingAsync(long userId)
    {
        if (userId == _client.Me?.Id)
            return false;

        await GetUserStateAsync(userId);
        await _pinLock.WaitAsync();
        try
        {
            _pins ??= await LoadPinsAsync();
            return _pins.TryGetValue(userId, out var pin) && pin.Epoch > (pin.AcceptedEpoch ?? pin.Epoch);
        }
        finally
        {
            _pinLock.Release();
        }
    }

    /// <summary>
    /// Accepts a user's current keys after they reset them. Pass the security
    /// number the person was shown; if the keys changed again since, nothing
    /// is accepted and the call fails.
    /// </summary>
    public async Task<TaskResult> AcceptKeyResetAsync(long userId, string expectedSafetyNumber)
    {
        var state = await GetUserStateAsync(userId, refresh: true);
        if (state?.HasIdentity != true)
            return TaskResult.FromFailure("This person's keys could not be loaded.");
        if (!MatchesSafetyNumber(state, expectedSafetyNumber))
            return TaskResult.FromFailure(KeysChangedMessage);

        await _pinLock.WaitAsync();
        try
        {
            _pins ??= await LoadPinsAsync();
            if (!_pins.TryGetValue(userId, out var pin) || pin.Epoch != state.Epoch)
                return TaskResult.FromFailure("This person's keys could not be loaded.");

            pin.AcceptedEpoch = state.Epoch;
            pin.AcceptedChangedAt = NowMs();
            await SavePinsAsync();
        }
        finally
        {
            _pinLock.Release();
        }

        // Direct chats re-check their keys against the accepted epoch.
        foreach (var key in _keyRings.Keys.Where(k => k.PlanetId == 0).ToList())
        {
            if (_keyRings.TryRemove(key, out _))
                ChannelKeysChanged?.Invoke(key.ChannelId);
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// The key epoch the person accepted for a user, or null when this device
    /// has not pinned them yet.
    /// </summary>
    private async Task<int?> GetAcceptedEpochAsync(long userId)
    {
        await _pinLock.WaitAsync();
        try
        {
            _pins ??= await LoadPinsAsync();
            return _pins.TryGetValue(userId, out var pin) ? pin.AcceptedEpoch ?? pin.Epoch : null;
        }
        finally
        {
            _pinLock.Release();
        }
    }

    private Task<Dictionary<long, KeyPin>> LoadPinsAsync() =>
        LoadStoredJsonAsync<Dictionary<long, KeyPin>>(PinsKey);

    /// <summary>
    /// Reads a JSON entry from the key store, or an empty value when the
    /// entry is missing or cannot be read.
    /// </summary>
    private async Task<T> LoadStoredJsonAsync<T>(string name) where T : class, new()
    {
        var bytes = await Store.GetAsync(StoreKey(name));
        if (bytes is null)
            return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(bytes) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }

    /// <summary>
    /// Saves pins, merged with what another tab or process stored in the
    /// meantime so one writer does not erase what another learned.
    /// </summary>
    private async Task SavePinsAsync()
    {
        foreach (var (userId, stored) in await LoadPinsAsync())
            _pins[userId] = _pins.TryGetValue(userId, out var mine) ? MergePins(mine, stored) : stored;

        await Store.SetAsync(StoreKey(PinsKey), JsonSerializer.SerializeToUtf8Bytes(_pins));
    }

    /// <summary>
    /// Combines two copies of a pin. The longer key log wins, since both
    /// extend the same history. The verification and the accepted epoch come
    /// from whichever copy changed them last, so removing a verification
    /// sticks; a verification made at an earlier epoch does not carry over.
    /// </summary>
    internal static KeyPin MergePins(KeyPin mine, KeyPin stored)
    {
        var log = stored.Count > mine.Count ? stored : mine;
        var verified = stored.VerifiedChangedAt > mine.VerifiedChangedAt ? stored : mine;
        var accepted = stored.AcceptedChangedAt > mine.AcceptedChangedAt ? stored : mine;
        return new KeyPin
        {
            Epoch = log.Epoch,
            Count = log.Count,
            HeadHash = log.HeadHash,
            Verified = verified.Verified && verified.Epoch == log.Epoch,
            VerifiedChangedAt = verified.VerifiedChangedAt,
            AcceptedEpoch = Math.Min(accepted.AcceptedEpoch ?? accepted.Epoch, log.Epoch),
            AcceptedChangedAt = accepted.AcceptedChangedAt
        };
    }

    private const string KeysChangedMessage =
        "This person's security code changed while it was on screen. Compare the new code first.";

    private static bool MatchesSafetyNumber(UserKeyState state, string expected) =>
        !string.IsNullOrEmpty(expected) && string.Equals(state?.SafetyNumber(), expected.Trim(), StringComparison.Ordinal);

    private async Task SavePinsLockedAsync()
    {
        await _pinLock.WaitAsync();
        try
        {
            if (_pins is not null)
                await SavePinsAsync();
        }
        finally
        {
            _pinLock.Release();
        }
    }

    /// <summary>
    /// The security number for a user. Two people who compare these in
    /// person, or over a call, know no one is intercepting their messages.
    /// </summary>
    public async Task<string> GetSafetyNumberAsync(long userId) =>
        (await GetUserStateAsync(userId))?.SafetyNumber();

    /// <summary>
    /// True when the person marked this user's security number as checked,
    /// and the user has not reset their keys since.
    /// </summary>
    public async Task<bool> IsVerifiedAsync(long userId)
    {
        await _pinLock.WaitAsync();
        try
        {
            _pins ??= await LoadPinsAsync();
            return _pins.TryGetValue(userId, out var pin) && pin.Verified;
        }
        finally
        {
            _pinLock.Release();
        }
    }

    /// <summary>
    /// Marks or unmarks a user's security number as checked. Marking requires
    /// the number the person compared; if the user's keys changed since it was
    /// shown, nothing is marked and the call fails.
    /// </summary>
    public async Task<TaskResult> SetVerifiedAsync(long userId, bool verified, string expectedSafetyNumber = null)
    {
        UserKeyState state = null;
        if (verified)
        {
            state = await GetUserStateAsync(userId, refresh: true);
            if (state?.HasIdentity != true)
                return TaskResult.FromFailure("This person's keys could not be loaded.");
            if (!MatchesSafetyNumber(state, expectedSafetyNumber))
                return TaskResult.FromFailure(KeysChangedMessage);
        }

        await _pinLock.WaitAsync();
        try
        {
            _pins ??= await LoadPinsAsync();
            if (!_pins.TryGetValue(userId, out var pin) || (state is not null && pin.Epoch != state.Epoch))
                return TaskResult.FromFailure("This person's keys could not be loaded.");

            pin.Verified = verified;
            pin.VerifiedChangedAt = NowMs();
            await SavePinsAsync();
            return TaskResult.SuccessResult;
        }
        finally
        {
            _pinLock.Release();
        }
    }

    /// <summary>
    /// True when the user has set up end-to-end encryption.
    /// </summary>
    public async Task<bool> HasEncryptionAsync(long userId)
    {
        try
        {
            return (await GetUserStateAsync(userId))?.HasIdentity == true;
        }
        catch (E2eeVerificationException)
        {
            return false;
        }
    }
}
