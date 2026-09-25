using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

/// <summary>
/// Whether this device can read and send end-to-end encrypted messages.
/// </summary>
public enum E2eeStatus
{
    /// <summary>Not loaded yet, or not logged in.</summary>
    Unknown = 0,

    /// <summary>The account has never set up encryption.</summary>
    NotSetUp = 1,

    /// <summary>
    /// The account uses encryption, but this device has not been linked or
    /// restored, so it cannot read encrypted messages yet. When
    /// <see cref="E2eeService.KeysResetElsewhere"/> is true, this device was
    /// set up before, the account's keys were then reset, and the device can
    /// still read the messages it could read before.
    /// </summary>
    NeedsVerification = 2,

    /// <summary>This device holds the account's keys.</summary>
    Ready = 3,

    /// <summary>The account's key log failed verification.</summary>
    Error = 4,

    /// <summary>
    /// The keys could not be loaded because the server could not be reached.
    /// The service retries on its own and when the connection returns;
    /// <see cref="E2eeService.InitializeAsync"/> retries right away.
    /// </summary>
    Unavailable = 5
}

/// <summary>
/// A notice about another user's security keys.
/// </summary>
public sealed class E2eeContactWarning
{
    public long UserId { get; init; }

    /// <summary>True when the user reset their keys, which is normal after losing every device.</summary>
    public bool KeysReset { get; init; }

    /// <summary>
    /// True when the server showed this device a key history that conflicts
    /// with one it saw before. This should never happen and may mean someone
    /// is trying to intercept messages.
    /// </summary>
    public bool Conflict { get; init; }
}

/// <summary>
/// End-to-end encryption for the SDK: this device's identity, linking and
/// recovery, channel keys, and encrypting and decrypting messages. The server
/// never receives a key that can read message text.
/// </summary>
public partial class E2eeService : ServiceBase
{
    private const string KeyPrefix = "valour-e2ee";

    /// <summary>The environment variable a bot can use to provide its recovery code.</summary>
    public const string RecoveryCodeEnvironmentVariable = "VALOUR_E2EE_RECOVERY_CODE";

    /// <summary>Raised when <see cref="Status"/> changes.</summary>
    public HybridEvent<E2eeStatus> StatusChanged;

    /// <summary>Raised when a device-link session for this account changes.</summary>
    public HybridEvent<DeviceLinkSessionDto> LinkSessionUpdated;

    /// <summary>Raised when keys for a channel arrive or change.</summary>
    public HybridEvent<long> ChannelKeysChanged;

    /// <summary>Raised when a recovery code is created or the person saves it.</summary>
    public HybridEvent RecoveryCodeChanged;

    /// <summary>Raised when another user's security keys change or conflict.</summary>
    public HybridEvent<E2eeContactWarning> ContactWarning;

    /// <summary>
    /// Raised when this account's keys were reset from somewhere else while
    /// this device was set up. See <see cref="KeysResetElsewhere"/>.
    /// </summary>
    public HybridEvent KeysResetElsewhereDetected;

    /// <summary>
    /// Raised when a decrypted message's search terms do not match its text,
    /// which means its sender tried to hide words from automod. The message is
    /// hidden; the event lets the app offer to report it.
    /// </summary>
    public HybridEvent<Models.Message> TamperedMessageDetected;

    public E2eeStatus Status { get; private set; }

    /// <summary>
    /// True when this device was set up for the account, and the account's
    /// keys were later reset from somewhere else. A reset is signed only by the
    /// new device it introduces, so the server could forge one; the device
    /// therefore keeps its keys and can still read the messages it could read
    /// before, but it cannot send until the person links or restores it.
    /// Apps should tell the person, who can check whether they reset their
    /// keys themselves.
    /// </summary>
    public bool KeysResetElsewhere { get; private set; }

    /// <summary>
    /// Where this device's private keys are stored. Set before logging in.
    /// Defaults to a file store for bots and a memory store in browsers.
    /// </summary>
    public IE2eeKeyStore KeyStore { get; set; }

    /// <summary>
    /// The name other devices see for this one, such as "Chrome on macOS".
    /// </summary>
    public string DeviceName { get; set; } = "Valour SDK";

    /// <summary>
    /// When true, the service sets up encryption automatically for an account
    /// that has never used it. Messages are always end-to-end encrypted, so
    /// apps and bots leave this on.
    /// </summary>
    public bool AutoSetUp { get; set; } = true;

    /// <summary>
    /// When true and this device has no working keys, the service restores
    /// them with the recovery code from the key store or from
    /// <see cref="RecoveryCode"/>. Bots use this because there is no person to
    /// link a device. Apps set it to false and guide the person instead.
    /// </summary>
    public bool AutoRecover { get; set; } = true;

    /// <summary>
    /// When true, the service resets the account's keys without asking if this
    /// device cannot restore them: when no device key is stored and no working
    /// recovery code is available, or when the stored device was removed from
    /// the account. A reset makes earlier messages unreadable and shows every
    /// contact a key change notice, so it defaults to false. A failure to reach
    /// the server never causes a reset.
    /// </summary>
    public bool AllowAutomaticReset { get; set; }

    /// <summary>
    /// A recovery code to restore this device with when it has no working
    /// keys, for bots whose key store does not survive restarts. Defaults to
    /// the <c>VALOUR_E2EE_RECOVERY_CODE</c> environment variable. Used only
    /// when <see cref="AutoRecover"/> is true.
    /// </summary>
    public string RecoveryCode { get; set; }

    /// <summary>
    /// When true, the service stores the recovery code it creates in the key
    /// store. Bots use this so a lost device key can be recovered unattended.
    /// </summary>
    public bool StoreRecoveryCode { get; set; } = true;

    /// <summary>
    /// A recovery code this device created that has not been confirmed as
    /// saved, or null. It is kept in memory only, so after a restart the
    /// person creates a new code instead. See <see cref="IsRecoveryCodeUnsavedAsync"/>.
    /// A bot can read the code here after its first setup and keep it in
    /// <c>VALOUR_E2EE_RECOVERY_CODE</c>.
    /// </summary>
    public string PendingRecoveryCode { get; private set; }

    private readonly ValourClient _client;
    private readonly SemaphoreSlim _identityLock = new(1, 1);

    // Cancelled when the account logs out, which stops background work such
    // as retries started for that account.
    private CancellationTokenSource _session = new();

    public E2eeService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, new LogOptions("E2EE", "#57a36b", "#a3333e", "#a39433"));

        KeyStore = OperatingSystem.IsBrowser()
            ? new MemoryE2eeKeyStore()
            : null;
        RecoveryCode = OperatingSystem.IsBrowser()
            ? null
            : Environment.GetEnvironmentVariable(RecoveryCodeEnvironmentVariable);

        _client.NodeService.NodeAdded += HookHubEvents;
        _client.NodeService.NodeReconnected += OnNodeReconnectedAsync;
    }

    private IE2eeKeyStore Store => KeyStore ??= new FileE2eeKeyStore(FileE2eeKeyStore.DefaultDirectory);

    private string StoreKey(string name) => $"{KeyPrefix}/u{_client.Me.Id}/{name}";

    // Names of the entries kept in the key store for each account.
    private const string DeviceKey = "device";
    private const string RecoveryCodeKey = "recovery-code";
    private const string RecoveryUnsavedKey = "recovery-unsaved";
    private const string PinsKey = "pins";
    private const string AccessPinsKey = "access-pins";
    private static string UserKeyGenerationKey(int generation) => $"user-key/{generation}";

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void SetStatus(E2eeStatus status)
    {
        if (Status == status)
            return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private void SetKeysResetElsewhere(bool value)
    {
        if (KeysResetElsewhere == value)
            return;
        KeysResetElsewhere = value;
        if (value)
            KeysResetElsewhereDetected?.Invoke();
    }

    /// <summary>
    /// Clears in-memory state, for example when logging out. Stored keys are
    /// not touched.
    /// </summary>
    public void Reset()
    {
        var session = _session;
        _session = new CancellationTokenSource();
        session.Cancel();
        session.Dispose();

        Device = null;
        MyKeyState = null;
        _pendingDevice = null;
        _pendingSessionId = null;
        _pendingLinkSecret = null;
        _scannedLinkCodes.Clear();
        _userKeys.Clear();
        _shownLinkSecrets.Clear();

        _userStates.Clear();
        _pins = null;
        _accessPins = null;
        _accessLogs.Clear();
        _checkpointsInProgress.Clear();

        _keyRings.Clear();
        _ringLocks.Clear();
        _lastKeyRequests.Clear();
        _scheduledServes.Clear();
        _generationPins.Clear();
        _directPeers.Clear();
        _memberMadeFirstKeys.Clear();

        _serverKeys.Clear();
        _serverKeysLoadedAt.Clear();
        _serverKeyFailures.Clear();
        _keyRingFailures.Clear();

        ResetMessageState();

        _initFailures = 0;
        Interlocked.Exchange(ref _initRetryScheduled, 0);
        PendingRecoveryCode = null;
        KeysResetElsewhere = false;
        SetStatus(E2eeStatus.Unknown);
    }

    // Realtime

    private void HookHubEvents(Node node)
    {
        node.HubConnection.On<E2eeRealtimeEvent>(E2eeRealtimeEvent.HubMethod, evt => OnRealtimeEventAsync(node, evt));
    }

    private async Task OnRealtimeEventAsync(Node node, E2eeRealtimeEvent evt)
    {
        if (evt is null || _client.Me is null)
            return;

        // A community node may only speak for planets it hosts.
        if (!node.AcceptsExternalPlanetRealtimeEvent(evt.PlanetId))
            return;

        try
        {
            switch (evt.Type)
            {
                case E2eeRealtimeEventTypes.LinkSession:
                    if (!node.IsExternal && evt.LinkSession is not null)
                        await OnLinkSessionUpdatedAsync(evt.LinkSession);
                    break;

                case E2eeRealtimeEventTypes.KeyLogUpdated:
                    if (!node.IsExternal && evt.UserId == _client.Me.Id)
                    {
                        await RefreshOwnStateAsync();
                    }
                    else if (evt.UserId is not null)
                    {
                        // Messages from a device this user just added can be
                        // read once their key log is loaded again.
                        _userStates.TryRemove(evt.UserId.Value, out _);
                        await RetryPendingMessagesFromAuthorAsync(evt.UserId.Value);
                    }
                    break;

                case E2eeRealtimeEventTypes.KeyRequest:
                    if (evt.ChannelId is not null && evt.UserId != _client.Me.Id)
                        ScheduleServe(evt.ChannelId.Value, evt.PlanetId);
                    break;

                case E2eeRealtimeEventTypes.KeysAvailable:
                    if (evt.ChannelId is not null)
                    {
                        // The keys are there now, so a fetch that failed
                        // moments ago is not left to hold back loading them.
                        ClearKeyRingFailure(evt.ChannelId.Value, evt.PlanetId);
                        await OnKeysAvailableAsync(evt.ChannelId.Value, evt.PlanetId);
                    }
                    break;

                case E2eeRealtimeEventTypes.AccessLogUpdated:
                    if (evt.PlanetId is not null)
                        _accessLogs.TryRemove((AccessLogScope.Planet, evt.PlanetId.Value), out _);
                    if (evt.ChannelId is not null)
                        _accessLogs.TryRemove((AccessLogScope.GroupChannel, evt.ChannelId.Value), out _);
                    break;

                case E2eeRealtimeEventTypes.AutomodTermsNeeded:
                    if (evt.PlanetId is not null)
                        await SyncAutomodTermsIfModeratorAsync(evt.PlanetId.Value);
                    break;
            }
        }
        catch (Exception e)
        {
            LogError($"Failed to handle encryption event {evt.Type}", e);
        }
    }

    /// <summary>
    /// Catches up on what realtime events would have said while the node was
    /// disconnected: this account's key log, other users' key logs, access
    /// logs, keys that arrived, and messages still waiting to be decrypted.
    /// </summary>
    private async Task OnNodeReconnectedAsync(Node node)
    {
        if (_client.Me is null)
            return;

        // Requests that failed while the connection was down may work now.
        _keyRingFailures.Clear();
        _serverKeyFailures.Clear();

        try
        {
            if (!node.IsExternal)
            {
                if (Status == E2eeStatus.Unavailable)
                {
                    await InitializeAsync();
                    return;
                }

                if (Status == E2eeStatus.Unknown)
                    return;

                await RefreshOwnStateAsync();
                _userStates.Clear();
            }

            _accessLogs.Clear();
            await ResyncPendingMessagesAsync();

            if (!node.IsExternal && Status == E2eeStatus.Ready)
                await ServePendingDirectRequestsAsync();
        }
        catch (Exception e)
        {
            LogError("Failed to catch up on encryption changes after reconnecting", e);
        }
    }

    // Retrying after the server could not be reached

    private static readonly TimeSpan MaxInitializeRetryDelay = TimeSpan.FromMinutes(5);
    private int _initFailures;
    private int _initRetryScheduled;

    /// <summary>
    /// Tries to load the keys again after a delay that grows with each
    /// failure: 5 seconds, then 10, 20, and so on up to 5 minutes. The count
    /// of failures is reset only once the keys load and nothing is left to
    /// retry, so a retry that fails at a later step still waits longer. Only
    /// one retry is scheduled at a time.
    /// </summary>
    private void ScheduleInitializeRetry()
    {
        if (Interlocked.Exchange(ref _initRetryScheduled, 1) == 1)
            return;

        var failures = Math.Min(Interlocked.Increment(ref _initFailures) - 1, 6);
        var delay = TimeSpan.FromSeconds(5 * Math.Pow(2, failures));
        if (delay > MaxInitializeRetryDelay)
            delay = MaxInitializeRetryDelay;
        delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));

        var token = _session.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                // Logged out; Reset cleared the scheduled flag.
                return;
            }

            // The flag is cleared before retrying, so a retry that fails can
            // schedule the next one. It is not cleared afterwards, which would
            // let a second chain of retries start beside that one.
            Interlocked.Exchange(ref _initRetryScheduled, 0);
            try
            {
                if (Status == E2eeStatus.Unavailable && _client.Me is not null && !token.IsCancellationRequested)
                    await InitializeAsync();
            }
            catch (Exception e)
            {
                LogError("Retrying to load encryption keys failed", e);
            }
        });
    }

    /// <summary>
    /// Forgets that loading a channel's keys failed recently, so the next
    /// request loads them instead of waiting out the failure's backoff.
    /// </summary>
    internal void ClearKeyRingFailure(long channelId, long? planetId) =>
        _keyRingFailures.TryRemove((planetId ?? 0, channelId), out _);

    /// <summary>
    /// True for failures that may succeed if tried again: the server could
    /// not be reached, it failed, or it asked the client to slow down.
    /// </summary>
    private static bool IsTransientFailure(int? code) => code is null or >= 500 or 408 or 429;

    // Helpers

    private Node HubNode => _client.PrimaryNode;

    private static string ChannelRoute(Models.Channel channel, string suffix) =>
        $"api/e2ee/channels/{channel.Id}/{suffix}" + (channel.PlanetId is null ? "" : $"?planetId={channel.PlanetId}");

    /// <summary>
    /// <see cref="ChannelRoute"/> followed by the separator for more query
    /// parameters, so callers can append <c>name=value</c> pairs.
    /// </summary>
    private static string ChannelRouteWith(Models.Channel channel, string suffix) =>
        ChannelRoute(channel, suffix) + (channel.PlanetId is null ? "?" : "&");

    private static TaskResult Fail(string message) => TaskResult.FromFailure(message);

    /// <summary>The outcome of a result without its data.</summary>
    private static TaskResult WithoutData(ITaskResult result) =>
        result.Success ? TaskResult.SuccessResult : Fail(result.Message);

    /// <summary>
    /// Finds a channel in the cache, fetching it when needed.
    /// </summary>
    private async ValueTask<Models.Channel> ResolveChannelAsync(long channelId, long? planetId)
    {
        if (planetId is not null)
        {
            if (!_client.Cache.Planets.TryGet(planetId.Value, out var planet))
                planet = await _client.PlanetService.FetchPlanetAsync(planetId.Value);
            if (planet is null)
                return null;
            if (planet.Channels.TryGet(channelId, out var planetChannel))
                return planetChannel;
            return await planet.FetchChannelAsync(channelId);
        }

        if (_client.Cache.Channels.TryGet(channelId, out var channel))
            return channel;
        return await _client.ChannelService.FetchDirectChannelAsync(channelId);
    }

    // Server attestation keys

    /// <summary>How soon the keys of a node are loaded again for a key ID they did not include.</summary>
    private static readonly TimeSpan ServerKeyReloadInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, byte[]> _serverKeys = new();
    private readonly ConcurrentDictionary<string, DateTime> _serverKeysLoadedAt = new();
    private readonly ConcurrentDictionary<string, FailureBackoff> _serverKeyFailures = new();

    // Channels whose key fetch failed recently, so it is not repeated for
    // every message on screen. Used by GetKeyRingAsync.
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), FailureBackoff> _keyRingFailures = new();

    /// <summary>
    /// Returns one server attestation key, reloading the node's keys once if
    /// it is not known yet, for example after the server created a new key.
    /// </summary>
    private async Task<byte[]> ResolveServerKeyAsync(Node node, string keyId) =>
        (await TryResolveServerKeyAsync(node, keyId)).Key;

    /// <summary>
    /// Returns one server attestation key. Unavailable is true when the key
    /// is not known and the node's keys could not be loaded just now, so the
    /// caller should try again later instead of treating the key as unknown.
    /// </summary>
    private async Task<(byte[] Key, bool Unavailable)> TryResolveServerKeyAsync(Node node, string keyId)
    {
        var id = node.Name + "|" + keyId;
        if (_serverKeys.TryGetValue(id, out var key))
            return (key, false);

        // The node's keys were loaded moments ago without this one; a new key
        // may have been created since, so the caller tries again later.
        if (_serverKeysLoadedAt.TryGetValue(node.Name, out var loadedAt) &&
            DateTime.UtcNow - loadedAt < ServerKeyReloadInterval)
            return (null, true);

        if (!await LoadServerKeysAsync(node))
            return (null, true);

        return (_serverKeys.GetValueOrDefault(id), false);
    }

    /// <summary>
    /// Loads a node's attestation keys. After a failure, requests pause for
    /// a while that grows with each failure, so rendering many messages does
    /// not repeat a request that just failed.
    /// </summary>
    private async Task<bool> LoadServerKeysAsync(Node node)
    {
        if (_serverKeyFailures.TryGetValue(node.Name, out var failure) && failure.IsWaiting)
            return false;

        var result = await node.GetJsonAsync<List<E2eeServerKeyDto>>("api/e2ee/server-keys", cacheDurationMs: 1000);
        if (!result.Success || result.Data is null)
        {
            _serverKeyFailures[node.Name] = (failure ?? new FailureBackoff()).Next();
            return false;
        }

        foreach (var key in result.Data)
            _serverKeys[node.Name + "|" + key.KeyId] = key.PublicKey;
        _serverKeysLoadedAt[node.Name] = DateTime.UtcNow;
        _serverKeyFailures.TryRemove(node.Name, out _);
        return true;
    }

    /// <summary>
    /// When a failed request may be tried again: 10 seconds after the first
    /// failure, then 20, then 30 at most.
    /// </summary>
    private sealed record FailureBackoff(int Failures = 0, DateTime RetryAt = default)
    {
        private static readonly TimeSpan First = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan Longest = TimeSpan.FromSeconds(30);

        public bool IsWaiting => DateTime.UtcNow < RetryAt;

        public FailureBackoff Next()
        {
            var delay = TimeSpan.FromTicks(First.Ticks * (Failures + 1));
            if (delay > Longest)
                delay = Longest;
            return new FailureBackoff(Failures + 1, DateTime.UtcNow + delay);
        }
    }
}
