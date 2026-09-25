using System.Collections.Concurrent;
using System.Text;
using Valour.Sdk.E2ee;
using Valour.Shared;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

/// <summary>
/// Result of starting a device link on a new device.
/// </summary>
public sealed class DeviceLinkStart
{
    public DeviceLinkSessionDto Session { get; init; }

    /// <summary>
    /// Text to show as a QR code. It holds a fingerprint of this device's
    /// keys and a secret the approving device must prove it read.
    /// </summary>
    public string QrCode { get; init; }
}

/// <summary>
/// A link code an existing device shows for a new device to scan or type.
/// </summary>
public sealed class DeviceLinkOffer
{
    public DeviceLinkSessionDto Session { get; init; }

    /// <summary>Text to show as a QR code.</summary>
    public string QrCode { get; init; }

    /// <summary>
    /// The same secret as 16 characters in four groups, for a new device
    /// without a camera.
    /// </summary>
    public string TypedCode { get; init; }
}

public partial class E2eeService
{
    /// <summary>This device's keys, when it is set up.</summary>
    public DeviceKeyPair Device { get; private set; }

    /// <summary>The verified state of this account's key log.</summary>
    public UserKeyState MyKeyState { get; private set; }

    private readonly ConcurrentDictionary<int, UserKeyPair> _userKeys = new();

    // Secrets for link codes this device is showing, by session ID.
    private readonly ConcurrentDictionary<string, byte[]> _shownLinkSecrets = new();

    // Codes this device scanned from new devices' screens, by session ID.
    private readonly ConcurrentDictionary<string, DeviceLinkCode> _scannedLinkCodes = new();

    // A device being linked keeps its new keys, and the secret the approval
    // must prove, here until approval arrives.
    private DeviceKeyPair _pendingDevice;
    private string _pendingSessionId;
    private byte[] _pendingLinkSecret;

    /// <summary>
    /// Raised when another device approved this one but the approval could
    /// not be used. The message says why. An approval that fails the link
    /// secret check is refused, because it did not come from a device that
    /// read or showed this link's code.
    /// </summary>
    public HybridEvent<string> LinkFailed;

    public UserKeyPair CurrentUserKey =>
        MyKeyState?.UserKey is null ? null : _userKeys.GetValueOrDefault(MyKeyState.UserKey.Generation);

    /// <summary>
    /// Loads this device's keys after login and works out whether it can read
    /// encrypted messages. Errors leave the service in a status the app can
    /// show; they do not fail login. When the server cannot be reached the
    /// status is <see cref="E2eeStatus.Unavailable"/> and the service tries
    /// again on its own; calling this again retries right away.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_client.Me is null)
            return;

        var wasUnavailable = Status == E2eeStatus.Unavailable;
        var unavailable = false;

        await _identityLock.WaitAsync();
        try
        {
            unavailable = !await LoadIdentityAsync();
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            LogError("Your key log failed verification", e);
            SetStatus(E2eeStatus.Error);
            return;
        }
        catch (Exception e)
        {
            LogError("Could not load your encryption keys", e);
            unavailable = true;
        }
        finally
        {
            if (unavailable)
                SetStatus(E2eeStatus.Unavailable);
            _identityLock.Release();
        }

        if (unavailable)
        {
            ScheduleInitializeRetry();
            return;
        }

        await AutoSetUpAsync();

        // Recovery can still fail to reach the server after the keys loaded;
        // the retry delay keeps growing until nothing is left to retry.
        if (Status != E2eeStatus.Unavailable)
            Interlocked.Exchange(ref _initFailures, 0);

        if (Status != E2eeStatus.Ready)
            return;

        _ = ServePendingDirectRequestsAsync();

        // Messages that arrived while the keys could not be loaded are
        // waiting; they can be read now.
        if (wasUnavailable)
            _ = RunInBackgroundAsync(ResyncPendingMessagesAsync, "Failed to decrypt waiting messages");
    }

    /// <summary>
    /// Loads the stored device and the account's key log and sets the status.
    /// Returns false when the server could not be reached.
    /// </summary>
    private async Task<bool> LoadIdentityAsync()
    {
        _userKeys.Clear();
        Device = await LoadDeviceAsync();

        var log = await FetchOwnLogAsync();
        if (log is null)
            return false;

        if (log.Count == 0)
        {
            MyKeyState = null;
            SetKeysResetElsewhere(false);
            await AdoptPendingKeysAsync(null);
            SetStatus(E2eeStatus.NotSetUp);
            return true;
        }

        var state = UserKeyLogVerifier.Verify(_client.Me.Id, log);
        await PinAsync(state, log);
        MyKeyState = state;
        await AdoptPendingKeysAsync(state);

        var activation = await TryActivateDeviceAsync();
        if (activation == Activation.Unavailable)
            return false;

        SetKeysResetElsewhere(activation == Activation.NotActive && Device is not null &&
                              WasRemovedByReset(state, Device.DeviceId));
        SetStatus(activation == Activation.Activated ? E2eeStatus.Ready : E2eeStatus.NeedsVerification);

        // A link that was waiting for approval when the app stopped can still
        // finish. It needs the identity lock this method runs under.
        if (activation == Activation.NotActive)
            _ = RunInBackgroundAsync(ResumePendingLinkAsync, "Could not finish linking this device");
        return true;
    }

    private async Task AutoSetUpAsync()
    {
        switch (Status)
        {
            case E2eeStatus.NotSetUp when AutoSetUp:
            {
                var result = await SetUpAsync();
                if (!result.Success)
                    LogError("Automatic encryption setup failed: " + result.Message);
                break;
            }
            case E2eeStatus.NeedsVerification:
                await AutoRecoverAsync();
                break;
        }
    }

    /// <summary>
    /// For a device without working keys on an account that has them: restores
    /// them with an available recovery code when <see cref="AutoRecover"/> is
    /// set, and resets them only when <see cref="AllowAutomaticReset"/> is set
    /// and nothing could restore them. A failure to reach the server leaves
    /// the device as it is and tries again later.
    /// </summary>
    private async Task AutoRecoverAsync()
    {
        WarnIfKeysWereNotKept();

        if (!AutoRecover && !AllowAutomaticReset)
            return;

        var code = AutoRecover ? await GetAvailableRecoveryCodeAsync() : null;
        var codeRejected = false;
        if (code is not null)
        {
            var (restored, transient) = await RestoreCoreAsync(code);
            if (restored.Success)
                return;

            if (transient)
            {
                LogWarning("Could not reach Valour to restore this device's keys; trying again later.");
                SetStatus(E2eeStatus.Unavailable);
                ScheduleInitializeRetry();
                return;
            }

            LogError("Restoring this device's keys with the recovery code failed: " + restored.Message);
            codeRejected = true;
        }

        // Only a definite cause justifies a reset: no stored device and no
        // working recovery code, or a stored device the verified key log no
        // longer lists as active.
        var noDeviceAndNoCode = Device is null && (code is null || codeRejected);
        var deviceRemoved = Device is not null && MyKeyState is not null &&
                            !MyKeyState.ActiveDevices.ContainsKey(Device.DeviceId);

        if (!AllowAutomaticReset || !(noDeviceAndNoCode || deviceRemoved))
        {
            LogError("This account has encryption keys, but this device cannot use them, so it cannot read or " +
                     "send messages. Keep the key store on persistent storage, provide the recovery code in " +
                     $"{nameof(RecoveryCode)} or the {RecoveryCodeEnvironmentVariable} environment variable, " +
                     $"link this device from another one, or set {nameof(AllowAutomaticReset)} to reset the " +
                     "keys, which makes earlier messages unreadable.");
            return;
        }

        LogWarning("Resetting this account's encryption keys because this device has no way to restore them. " +
                   "Earlier encrypted messages become unreadable to this account.");
        var (reset, resetTransient) = await ResetCoreAsync();
        if (reset.Success)
            return;

        LogError("Automatic encryption reset failed: " + reset.Message);
        if (resetTransient)
        {
            SetStatus(E2eeStatus.Unavailable);
            ScheduleInitializeRetry();
        }
    }

    /// <summary>
    /// The recovery code this device keeps in its key store when
    /// <see cref="StoreRecoveryCode"/> is true, or null. A bot operator can
    /// read it once and keep it in <c>VALOUR_E2EE_RECOVERY_CODE</c>, so the bot
    /// can restore its keys if the key store is lost.
    /// </summary>
    public async Task<string> GetStoredRecoveryCodeAsync()
    {
        if (_client.Me is null)
            return null;
        var stored = await Store.GetAsync(StoreKey(RecoveryCodeKey));
        return stored is null ? null : Encoding.UTF8.GetString(stored);
    }

    /// <summary>
    /// The recovery code stored by this device, or the one provided in
    /// <see cref="RecoveryCode"/>.
    /// </summary>
    private async Task<string> GetAvailableRecoveryCodeAsync() =>
        await GetStoredRecoveryCodeAsync() ?? (string.IsNullOrWhiteSpace(RecoveryCode) ? null : RecoveryCode.Trim());

    /// <summary>
    /// Warns loudly when an account with keys finds no device key in this
    /// store, which usually means the store does not survive restarts.
    /// </summary>
    private void WarnIfKeysWereNotKept()
    {
        if (Device is not null || MyKeyState?.HasIdentity != true)
            return;

        switch (Store)
        {
            case FileE2eeKeyStore files:
                LogError($"No encryption keys were found in {files.DirectoryPath}" +
                         (files.CreatedDirectory ? ", which did not exist before this run," : "") +
                         " but this account already has keys. The directory must survive restarts: keep it " +
                         "on persistent storage, such as a mounted volume for a container, or provide the " +
                         $"recovery code in the {RecoveryCodeEnvironmentVariable} environment variable.");
                break;
            case MemoryE2eeKeyStore:
                LogWarning("This device keeps its encryption keys in memory, and this account already has keys. " +
                           "Keys in memory are lost when the program exits.");
                break;
        }
    }

    private async Task<List<UserKeyLogEntry>> FetchOwnLogAsync()
    {
        var result = await HubNode.GetJsonAsync<List<UserKeyLogEntry>>(
            $"api/e2ee/users/{_client.Me.Id}/log", cacheDurationMs: null);
        return result.Success ? result.Data ?? [] : null;
    }

    private async Task<DeviceKeyPair> LoadDeviceAsync()
    {
        var bytes = await Store.GetAsync(StoreKey(DeviceKey));
        if (bytes is null)
            return null;

        try
        {
            return DeviceKeyPair.Deserialize(bytes);
        }
        catch (E2eeFormatException)
        {
            await Store.RemoveAsync(StoreKey(DeviceKey));
            return null;
        }
    }

    private enum Activation
    {
        /// <summary>The device is active and holds the current user key.</summary>
        Activated,

        /// <summary>There is no device, or the key log does not list it as active.</summary>
        NotActive,

        /// <summary>The user key could not be fetched from the server.</summary>
        Unavailable
    }

    /// <summary>
    /// Makes this device ready if it is an active device in the key log,
    /// loading or fetching the current user key.
    /// </summary>
    private async Task<Activation> TryActivateDeviceAsync()
    {
        if (Device is null || MyKeyState is null || !MyKeyState.ActiveDevices.ContainsKey(Device.DeviceId))
            return Activation.NotActive;

        var current = MyKeyState.UserKey.Generation;
        var key = await LoadUserKeyAsync(current);
        if (key is null)
        {
            var box = await HubNode.GetJsonAsync<UserKeyBoxDto>(
                $"api/e2ee/users/me/boxes/{Uri.EscapeDataString(Device.DeviceId)}?generation={current}",
                allow404: true, cacheDurationMs: null);
            if (!box.Success)
                return Activation.Unavailable;
            if (box.Data is null)
                return Activation.NotActive;

            key = UserKeyPair.OpenBox(_client.Me.Id, current, Device.DeviceId, Device.EncryptPrivateKey, box.Data.Box);
            if (!key.EncryptPublicKey.AsSpan().SequenceEqual(MyKeyState.UserKey.EncryptPublicKey))
                throw new E2eeVerificationException("The user key does not match the key log.");
            await StoreUserKeyAsync(key);
        }

        if (!key.EncryptPublicKey.AsSpan().SequenceEqual(MyKeyState.UserKey.EncryptPublicKey))
            throw new E2eeVerificationException("The user key does not match the key log.");

        _userKeys[key.Generation] = key;
        return Activation.Activated;
    }

    /// <summary>
    /// True when a device was replaced by a reset rather than removed by one
    /// of the account's own devices or its recovery code.
    /// </summary>
    internal static bool WasRemovedByReset(UserKeyState state, string deviceId) =>
        state.EverDevices.ContainsKey(deviceId) && !state.ActiveDevices.ContainsKey(deviceId) &&
        !WasRevokedDirectly(state, deviceId);

    /// <summary>
    /// True when a signed <c>RevokeDevice</c> entry removed the device. Only
    /// an active device of the account or its recovery key can sign one.
    /// </summary>
    internal static bool WasRevokedDirectly(UserKeyState state, string deviceId) =>
        state.Records.Any(r => r.Type == UserKeyLogEntryType.RevokeDevice && r.TargetDeviceId == deviceId);

    // Keys written before the server accepts them

    // A new device key and recovery code are stored as pending before they
    // are appended to the key log, so a crash or lost response after the
    // server accepted them cannot lose them. The next start adopts what the
    // key log lists and discards the rest.
    private const string PendingDeviceKey = "pending-device";
    private const string PendingRecoveryKey = "pending-recovery";
    private static readonly TimeSpan PendingKeyLifetime = TimeSpan.FromMinutes(15);

    private Task SavePendingDeviceAsync(DeviceKeyPair device) =>
        Store.SetAsync(StoreKey(PendingDeviceKey), WithTimestamp(device.Serialize()));

    private Task SavePendingRecoveryAsync(RecoveryKey recovery) =>
        Store.SetAsync(StoreKey(PendingRecoveryKey), WithTimestamp(Encoding.UTF8.GetBytes(recovery.ToCode())));

    private static byte[] WithTimestamp(byte[] value)
    {
        var bytes = new byte[8 + value.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, NowMs());
        value.CopyTo(bytes, 8);
        return bytes;
    }

    private async Task<(byte[] Value, bool Expired)> LoadPendingAsync(string name)
    {
        var bytes = await Store.GetAsync(StoreKey(name));
        if (bytes is null || bytes.Length < 8)
            return (null, bytes is not null);
        var createdMs = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes);
        var expired = NowMs() - createdMs > (long)PendingKeyLifetime.TotalMilliseconds;
        return (bytes[8..], expired);
    }

    /// <summary>
    /// Removes the stored pending device if it is the given one, so another
    /// tab or process that is linking its own device keeps its key.
    /// </summary>
    private async Task ClearPendingDeviceAsync(string deviceId)
    {
        try
        {
            var (value, _) = await LoadPendingAsync(PendingDeviceKey);
            if (value is not null && TryReadDevice(value)?.DeviceId != deviceId)
                return;
            await Store.RemoveAsync(StoreKey(PendingDeviceKey));
        }
        catch (Exception e)
        {
            LogWarning($"Could not remove a pending device key: {e.Message}");
        }
    }

    private async Task ClearPendingRecoveryAsync()
    {
        try
        {
            await Store.RemoveAsync(StoreKey(PendingRecoveryKey));
        }
        catch (Exception e)
        {
            LogWarning($"Could not remove a pending recovery code: {e.Message}");
        }
    }

    private static DeviceKeyPair TryReadDevice(byte[] bytes)
    {
        try
        {
            return DeviceKeyPair.Deserialize(bytes);
        }
        catch (E2eeFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finishes keys a previous run stored as pending: a device the key log
    /// now lists becomes this device, and a recovery code the key log now
    /// names is shown to the person. Pending keys the log does not list are
    /// kept for a while, in case a link is still waiting for approval, and
    /// then discarded.
    /// </summary>
    private async Task AdoptPendingKeysAsync(UserKeyState state)
    {
        var (deviceBytes, deviceExpired) = await LoadPendingAsync(PendingDeviceKey);
        if (deviceBytes is not null || deviceExpired)
        {
            var pending = deviceBytes is null ? null : TryReadDevice(deviceBytes);
            if (pending is not null && state is not null && state.ActiveDevices.ContainsKey(pending.DeviceId))
            {
                if (Device is null || !state.ActiveDevices.ContainsKey(Device.DeviceId))
                {
                    Device = pending;
                    await Store.SetAsync(StoreKey(DeviceKey), pending.Serialize());
                    Log("Finished saving this device's keys, which an interrupted setup left unsaved.");
                }
                await Store.RemoveAsync(StoreKey(PendingDeviceKey));
            }
            else if (pending is null || deviceExpired || state?.EverDevices.ContainsKey(pending.DeviceId) == true ||
                     pending.DeviceId == Device?.DeviceId)
            {
                if (_pendingDevice is null || pending?.DeviceId != _pendingDevice.DeviceId)
                    await Store.RemoveAsync(StoreKey(PendingDeviceKey));
            }
        }

        var (codeBytes, codeExpired) = await LoadPendingAsync(PendingRecoveryKey);
        if (codeBytes is null && !codeExpired)
            return;

        if (codeBytes is not null && RecoveryKey.TryParse(Encoding.UTF8.GetString(codeBytes), out var recovery) &&
            state?.Recovery is not null && state.Recovery.DeviceId == recovery.RecoveryId)
        {
            if (StoreRecoveryCode)
                await Store.SetAsync(StoreKey(RecoveryCodeKey), Encoding.UTF8.GetBytes(recovery.ToCode()));
            await MarkRecoveryCodeUnsavedAsync(recovery);
            await Store.RemoveAsync(StoreKey(PendingRecoveryKey));
        }
        else if (codeBytes is null || codeExpired)
        {
            await Store.RemoveAsync(StoreKey(PendingRecoveryKey));
        }
    }

    /// <summary>
    /// Makes newly appended keys this device's keys. The in-memory state, the
    /// status, and any new recovery code are set first, so a failure to write
    /// storage still leaves the device working and the code visible; the
    /// pending copy stays in storage until the final copy is written.
    /// </summary>
    private async Task AdoptNewKeysAsync(DeviceKeyPair device, UserKeyPair userKey, RecoveryKey newRecovery,
        RecoveryKey recoveryToStore)
    {
        Device = device;
        _userKeys[userKey.Generation] = userKey;
        _keyRings.Clear();
        SetKeysResetElsewhere(false);
        SetStatus(E2eeStatus.Ready);

        if (newRecovery is not null)
            await MarkRecoveryCodeUnsavedAsync(newRecovery);

        try
        {
            await Store.SetAsync(StoreKey(DeviceKey), device.Serialize());
            await Store.SetAsync(StoreKey(UserKeyGenerationKey(userKey.Generation)), userKey.Serialize());
            if (recoveryToStore is not null && StoreRecoveryCode)
                await Store.SetAsync(StoreKey(RecoveryCodeKey), Encoding.UTF8.GetBytes(recoveryToStore.ToCode()));

            await ClearPendingDeviceAsync(device.DeviceId);
            if (newRecovery is not null)
                await ClearPendingRecoveryAsync();
        }
        catch (Exception e)
        {
            LogError("Your new encryption keys are in use, but saving them on this device failed. " +
                     "They are saved on the next start if storage works again.", e);
        }
    }

    /// <summary>
    /// Removes pending keys after the server refused them, unless it may have
    /// accepted them: then they stay for the next start to check, and the
    /// service retries loading.
    /// </summary>
    private async Task AbandonPendingKeysAsync(string deviceId, bool recovery, bool transient)
    {
        if (transient)
        {
            SetStatus(E2eeStatus.Unavailable);
            ScheduleInitializeRetry();
            return;
        }

        if (deviceId is not null)
            await ClearPendingDeviceAsync(deviceId);
        if (recovery)
            await ClearPendingRecoveryAsync();
    }

    /// <summary>
    /// Runs work started without waiting for it, logging any failure.
    /// </summary>
    private async Task RunInBackgroundAsync(Func<Task> work, string failure)
    {
        try
        {
            await work();
        }
        catch (Exception e)
        {
            LogError(failure, e);
        }
    }

    private async Task<UserKeyPair> LoadUserKeyAsync(int generation)
    {
        var bytes = await Store.GetAsync(StoreKey(UserKeyGenerationKey(generation)));
        return bytes is null ? null : UserKeyPair.Deserialize(bytes);
    }

    private async Task StoreUserKeyAsync(UserKeyPair key)
    {
        _userKeys[key.Generation] = key;
        await Store.SetAsync(StoreKey(UserKeyGenerationKey(key.Generation)), key.Serialize());
    }

    /// <summary>
    /// Returns a user key generation, unwrapping older generations from the
    /// current one when needed. Generations from before a reset are only
    /// available if this device stored them.
    /// </summary>
    public async Task<UserKeyPair> GetUserKeyAsync(int generation)
    {
        if (_userKeys.TryGetValue(generation, out var cached))
            return cached;

        var stored = await LoadUserKeyAsync(generation);
        if (stored is not null)
        {
            _userKeys[generation] = stored;
            return stored;
        }

        if (MyKeyState is null || generation < MyKeyState.EpochFirstGeneration || generation > MyKeyState.UserKey.Generation)
            return null;

        var key = CurrentUserKey;
        while (key is not null && key.Generation > generation)
        {
            if (!MyKeyState.PreviousUserKeyWrapped.TryGetValue(key.Generation, out var wrapped))
                return null;
            key = key.UnwrapPrevious(_client.Me.Id, wrapped);
            await StoreUserKeyAsync(key);
        }

        return key;
    }

    private async Task RefreshOwnStateAsync()
    {
        var log = await FetchOwnLogAsync();
        if (log is null || log.Count == 0)
            return;

        await _identityLock.WaitAsync();
        try
        {
            MyKeyState = UserKeyLogVerifier.Verify(_client.Me.Id, log);
            await PinAsync(MyKeyState, log);
            if (Status == E2eeStatus.Unknown)
                return;

            if (Device is not null && !MyKeyState.ActiveDevices.ContainsKey(Device.DeviceId) &&
                MyKeyState.EverDevices.ContainsKey(Device.DeviceId))
            {
                // Another tab or process of this app may have stored a newer
                // device for the account in the same storage.
                if (await TryAdoptStoredDeviceAsync())
                    return;

                if (WasRevokedDirectly(MyKeyState, Device.DeviceId))
                {
                    // One of the account's own devices, or its recovery code,
                    // removed this device.
                    await ForgetLocalKeysAsync();
                    SetKeysResetElsewhere(false);
                }
                else
                {
                    // A reset is signed only by the new device it introduces,
                    // so the server could forge one. The keys stay, so this
                    // device can still read what it could read before.
                    LogWarning("This account's encryption keys were reset from another sign-in. This device keeps " +
                               "its keys for earlier messages. If the person did not reset them, their password " +
                               "may be compromised and they should reset the keys from this device.");
                    SetKeysResetElsewhere(true);
                }

                SetStatus(E2eeStatus.NeedsVerification);
                return;
            }

            switch (await TryActivateDeviceAsync())
            {
                case Activation.Activated:
                    SetKeysResetElsewhere(false);
                    SetStatus(E2eeStatus.Ready);
                    break;
                case Activation.NotActive:
                    if (!await TryAdoptStoredDeviceAsync() && Status != E2eeStatus.NotSetUp)
                        SetStatus(E2eeStatus.NeedsVerification);
                    break;
                case Activation.Unavailable:
                    // The status is settled by the next refresh or retry.
                    break;
            }
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            LogError("Your key log failed verification", e);
            SetStatus(E2eeStatus.Error);
        }
        finally
        {
            _identityLock.Release();
        }
    }

    /// <summary>
    /// Switches to a device key that another tab or process of this app
    /// stored for the account, if the key log lists it as active.
    /// </summary>
    private async Task<bool> TryAdoptStoredDeviceAsync()
    {
        var stored = TryReadDevice(await Store.GetAsync(StoreKey(DeviceKey)) ?? []);
        if (stored is null || stored.DeviceId == Device?.DeviceId || MyKeyState is null ||
            !MyKeyState.ActiveDevices.ContainsKey(stored.DeviceId))
            return false;

        var previous = Device;
        Device = stored;
        if (await TryActivateDeviceAsync() == Activation.Activated)
        {
            _keyRings.Clear();
            SetKeysResetElsewhere(false);
            SetStatus(E2eeStatus.Ready);
            return true;
        }

        Device = previous;
        return false;
    }

    /// <summary>
    /// Deletes this device's keys after one of the account's devices removed
    /// it. Storage is shared by every tab of the app, so when it holds a
    /// different device, which another tab stored, nothing is deleted and
    /// only this instance forgets its keys.
    /// </summary>
    private async Task ForgetLocalKeysAsync()
    {
        var ours = Device;
        var stored = await Store.GetAsync(StoreKey(DeviceKey));
        var storedDevice = stored is null ? null : TryReadDevice(stored);

        Device = null;
        _userKeys.Clear();
        _keyRings.Clear();

        if (ours is not null && storedDevice is not null && storedDevice.DeviceId != ours.DeviceId)
            return;

        if (MyKeyState is not null)
        {
            foreach (var generation in MyKeyState.UserKeyGenerations.Keys)
                await Store.RemoveAsync(StoreKey(UserKeyGenerationKey(generation)));
        }

        await Store.RemoveAsync(StoreKey(DeviceKey));
    }

    /// <summary>
    /// Appends an entry to this account's key log and reloads it. Uncertain
    /// is true when the server may have accepted the entry even though the
    /// result says otherwise, because it could not be reached or the log
    /// could not be reloaded afterwards.
    /// </summary>
    private async Task<(TaskResult<UserKeyState> Result, bool Uncertain)> AppendOwnLogAsync(UserKeyLogEntry entry,
        List<UserKeyBoxDto> boxes)
    {
        var result = await HubNode.PostAsyncWithResponse<UserKeyLogEntry>("api/e2ee/users/me/log",
            new AppendKeyLogRequest { Entry = entry, Boxes = boxes });
        if (!result.Success)
            return (TaskResult<UserKeyState>.FromFailure(result.Message), IsTransientFailure(result.Code));

        var log = await FetchOwnLogAsync();
        if (log is null)
            return (TaskResult<UserKeyState>.FromFailure(
                "Your keys were saved, but they could not be loaded again. Valour tries again automatically."), true);

        var state = UserKeyLogVerifier.Verify(_client.Me.Id, log);
        await PinAsync(state, log);
        MyKeyState = state;
        return (TaskResult<UserKeyState>.FromData(state), false);
    }

    // Setup

    /// <summary>
    /// Sets up encryption for an account that has never used it. Returns the
    /// recovery code, which the person must save.
    /// </summary>
    public async Task<TaskResult<string>> SetUpAsync()
    {
        await _identityLock.WaitAsync();
        try
        {
            if (Status != E2eeStatus.NotSetUp)
                return TaskResult<string>.FromFailure("Encryption is already set up for this account.");

            var device = DeviceKeyPair.Generate();
            var userKey = UserKeyPair.Generate(1);
            var recovery = RecoveryKey.Generate();
            var entry = UserKeyLogBuilder.Genesis(_client.Me.Id, device, DeviceName, userKey, recovery, NowMs());
            var boxes = new List<UserKeyBoxDto>
            {
                UserKeyBox(userKey, device.DeviceId, device.EncryptPublicKey),
                UserKeyBox(userKey, recovery.RecoveryId, recovery.EncryptPublicKey)
            };

            var saved = await TrySavePendingAsync(device, recovery);
            if (!saved.Success)
                return TaskResult<string>.FromFailure(saved.Message);

            var (result, uncertain) = await AppendOwnLogAsync(entry, boxes);
            if (!result.Success)
            {
                await AbandonPendingKeysAsync(device.DeviceId, recovery: true, uncertain);
                return TaskResult<string>.FromFailure(result.Message);
            }

            await AdoptNewKeysAsync(device, userKey, newRecovery: recovery, recoveryToStore: recovery);
            return TaskResult<string>.FromData(recovery.ToCode());
        }
        finally
        {
            _identityLock.Release();
        }
    }

    /// <summary>
    /// Stores a new device key and recovery code as pending before the server
    /// learns of them.
    /// </summary>
    private async Task<TaskResult> TrySavePendingAsync(DeviceKeyPair device, RecoveryKey recovery)
    {
        try
        {
            if (device is not null)
                await SavePendingDeviceAsync(device);
            if (recovery is not null)
                await SavePendingRecoveryAsync(recovery);
            return TaskResult.SuccessResult;
        }
        catch (Exception e)
        {
            LogError("Could not save new encryption keys on this device", e);
            return Fail("Your new encryption keys could not be saved on this device, so nothing was changed. " +
                        e.Message);
        }
    }

    /// <summary>
    /// True when this device created a recovery code the person has not
    /// confirmed saving. Apps remind the person to create and save one.
    /// </summary>
    public async Task<bool> IsRecoveryCodeUnsavedAsync() =>
        Status == E2eeStatus.Ready && _client.Me is not null &&
        await Store.GetAsync(StoreKey(RecoveryUnsavedKey)) is not null;

    /// <summary>Records that the person saved the current recovery code.</summary>
    public async Task MarkRecoveryCodeSavedAsync()
    {
        PendingRecoveryCode = null;
        if (_client.Me is not null)
            await Store.RemoveAsync(StoreKey(RecoveryUnsavedKey));
        RecoveryCodeChanged?.Invoke();
    }

    /// <summary>
    /// Remembers a new recovery code until the person confirms saving it. A
    /// code stored for unattended recovery needs no reminder, but is still
    /// available in <see cref="PendingRecoveryCode"/> until the program exits.
    /// </summary>
    private async Task MarkRecoveryCodeUnsavedAsync(RecoveryKey recovery)
    {
        PendingRecoveryCode = recovery.ToCode();
        if (!StoreRecoveryCode)
        {
            try
            {
                await Store.SetAsync(StoreKey(RecoveryUnsavedKey), [1]);
            }
            catch (Exception e)
            {
                LogError("Could not store the reminder to save the recovery code", e);
            }
        }

        RecoveryCodeChanged?.Invoke();
    }

    private UserKeyBoxDto UserKeyBox(UserKeyPair key, string recipientId, byte[] recipientEncryptPublicKey) => new()
    {
        UserId = _client.Me.Id,
        Generation = key.Generation,
        RecipientId = recipientId,
        Box = key.SealTo(_client.Me.Id, recipientId, recipientEncryptPublicKey)
    };

    // Linking a new device (this device is new)

    // A device being linked is kept here, with its session and the link
    // secret, until the approval arrives, so an app that restarts meanwhile
    // can still finish. It is separate from the pending device of setup and
    // restore, because a linked device is only used once the approval proof
    // checks out.
    private const string PendingLinkKey = "pending-link";

    private const string ApprovalRejectedMessage =
        "This device refused an approval that did not come from a device that saw this link's code. " +
        "Link it again from a device you trust, or use your recovery code.";

    /// <summary>
    /// Starts linking this device by showing a QR code that another of the
    /// person's devices scans.
    /// </summary>
    public async Task<TaskResult<DeviceLinkStart>> StartLinkingAsync()
    {
        if (Status != E2eeStatus.NeedsVerification)
            return TaskResult<DeviceLinkStart>.FromFailure("This device does not need to be linked.");

        var device = DeviceKeyPair.Generate();
        var secret = E2eeCrypto.RandomBytes(DeviceLinkCode.NewDeviceSecretLength);
        var descriptor = UserKeyLogBuilder.Describe(_client.Me.Id, device, DeviceName);
        var result = await HubNode.PostAsyncWithResponse<DeviceLinkSessionDto>("api/e2ee/link-sessions",
            new CreateDeviceLinkRequest
            {
                Mode = DeviceLinkMode.NewDeviceShowsCode,
                Device = DeviceDescriptorDto.From(descriptor)
            });
        if (!result.Success)
            return TaskResult<DeviceLinkStart>.FromFailure(result.Message);

        var saved = await BeginPendingLinkAsync(device, result.Data.Id, secret);
        if (!saved.Success)
        {
            await HubNode.PostAsync($"api/e2ee/link-sessions/{result.Data.Id}/deny", (object)null);
            return TaskResult<DeviceLinkStart>.FromFailure(saved.Message);
        }

        return TaskResult<DeviceLinkStart>.FromData(new DeviceLinkStart
        {
            Session = result.Data,
            QrCode = new DeviceLinkCode(DeviceLinkMode.NewDeviceShowsCode, _client.Me.Id, result.Data.Id,
                device.PublicKeys.Fingerprint(), secret).ToString()
        });
    }

    /// <summary>
    /// Links this device with a code shown on another of the person's
    /// devices: the text of its QR code, or the 16-character code typed by
    /// the person. The other device then asks the person to approve.
    /// </summary>
    public async Task<TaskResult<DeviceLinkSessionDto>> JoinLinkAsync(string code)
    {
        if (Status != E2eeStatus.NeedsVerification)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("This device does not need to be linked.");

        string sessionId;
        byte[] secret;
        if (DeviceLinkCode.TryParse(code, out var scanned))
        {
            if (scanned.Mode != DeviceLinkMode.ExistingDeviceShowsCode)
                return TaskResult<DeviceLinkSessionDto>.FromFailure(
                    "That code is for approving a new device. Scan it with a device you already use.");
            if (scanned.UserId != _client.Me.Id)
                return TaskResult<DeviceLinkSessionDto>.FromFailure("That code belongs to a different account.");
            sessionId = scanned.SessionId;
            secret = scanned.Secret;
        }
        else if (DeviceLinkTypedCode.TryParse(code, out secret))
        {
            sessionId = DeviceLinkVerification.SessionIdFor(secret, _client.Me.Id);
        }
        else
        {
            return TaskResult<DeviceLinkSessionDto>.FromFailure(
                "That is not a Valour device link code. Check the code on your other device.");
        }

        var device = DeviceKeyPair.Generate();
        var descriptor = UserKeyLogBuilder.Describe(_client.Me.Id, device, DeviceName);
        var result = await HubNode.PostAsyncWithResponse<DeviceLinkSessionDto>(
            $"api/e2ee/link-sessions/{Uri.EscapeDataString(sessionId)}/join",
            new JoinDeviceLinkRequest
            {
                Device = DeviceDescriptorDto.From(descriptor),
                JoinMac = DeviceLinkVerification.JoinMac(secret, _client.Me.Id, sessionId, device.PublicKeys)
            });
        if (!result.Success)
            return result;

        var saved = await BeginPendingLinkAsync(device, sessionId, secret);
        if (!saved.Success)
        {
            await HubNode.PostAsync($"api/e2ee/link-sessions/{Uri.EscapeDataString(sessionId)}/deny", (object)null);
            return TaskResult<DeviceLinkSessionDto>.FromFailure(saved.Message);
        }

        return result;
    }

    /// <summary>
    /// Cancels a link this device started.
    /// </summary>
    public async Task CancelLinkingAsync()
    {
        var sessionId = _pendingSessionId;
        var device = _pendingDevice;
        if (device is not null)
            await EndPendingLinkAsync(device);
        if (sessionId is not null)
            await HubNode.PostAsync($"api/e2ee/link-sessions/{Uri.EscapeDataString(sessionId)}/deny", (object)null);
    }

    private async Task OnLinkSessionUpdatedAsync(DeviceLinkSessionDto session)
    {
        LinkSessionUpdated?.Invoke(session);

        var device = _pendingDevice;
        if (device is null || session.Id != _pendingSessionId)
            return;

        if (session.Status == DeviceLinkStatus.Approved)
        {
            var result = await CompleteLinkAsync();
            if (!result.Success)
                LinkFailed?.Invoke(result.Message);
        }
        else if (session.Status is DeviceLinkStatus.Denied or DeviceLinkStatus.Expired)
        {
            await EndPendingLinkAsync(device);
        }
    }

    /// <summary>
    /// Finishes linking once another device approved. Also called by apps that
    /// poll the session instead of waiting for the realtime event.
    ///
    /// The server relays the approval, and a reset the server forged would
    /// give it a device that can sign one. So the approval is used only if
    /// the approving device proved, with the secret from this link's code,
    /// that it made the exact key log entry and user key box this device
    /// receives. Otherwise the approval is refused.
    /// </summary>
    public async Task<TaskResult> CompleteLinkAsync()
    {
        var device = _pendingDevice;
        var sessionId = _pendingSessionId;
        var secret = _pendingLinkSecret;

        await _identityLock.WaitAsync();
        try
        {
            // The realtime event and an app that polls can both finish the
            // same link; the second call reports how the first one ended.
            if (device is null || sessionId is null || secret is null || _pendingDevice != device)
                return Status == E2eeStatus.Ready ? TaskResult.SuccessResult : Fail("No link is in progress.");

            var session = await HubNode.GetJsonAsync<DeviceLinkSessionDto>(
                $"api/e2ee/link-sessions/{Uri.EscapeDataString(sessionId)}", allow404: true, cacheDurationMs: null);
            if (!session.Success)
                return Fail("Could not reach Valour. Try again.");
            if (session.Data is null || session.Data.Status is DeviceLinkStatus.Denied or DeviceLinkStatus.Expired)
            {
                await EndPendingLinkAsync(device);
                return Fail("This link request has ended. Start again.");
            }

            if (session.Data.Status != DeviceLinkStatus.Approved)
                return Fail("This device has not been approved yet.");

            var (log, state, error) = await FetchVerifiedOwnLogAsync();
            if (error is not null)
                return Fail(error);

            var added = state.Records.LastOrDefault(r =>
                r.Type == UserKeyLogEntryType.AddDevice && r.Device?.Keys.DeviceId == device.DeviceId);
            var entry = added is null ? null : log.FirstOrDefault(e => e.Seq == added.Seq);
            if (entry is null || !state.ActiveDevices.ContainsKey(device.DeviceId))
                return Fail("This device has not been approved yet.");

            // The approving device sealed the user key current when it added
            // this device; later generations are checked against the log.
            var atApproval = UserKeyLogVerifier.Verify(_client.Me.Id, log.Where(e => e.Seq <= added.Seq).ToList());
            var generation = atApproval.UserKey.Generation;
            var box = await FetchOwnBoxAsync(device.DeviceId, generation);
            if (!box.Success)
                return Fail(box.Message);

            if (!DeviceLinkVerification.VerifyApprovalMac(secret, _client.Me.Id, sessionId, device.DeviceId,
                    entry.Body, generation, box.Data.Box, session.Data.ApprovalPins, session.Data.ApprovalMac))
            {
                LogError("A device-link approval failed the link secret check and was refused. It did not come " +
                         "from a device that saw this link's code.");
                await EndPendingLinkAsync(device);
                return Fail(ApprovalRejectedMessage);
            }

            var userKey = UserKeyPair.OpenBox(_client.Me.Id, generation, device.DeviceId, device.EncryptPrivateKey,
                box.Data.Box);
            if (!userKey.EncryptPublicKey.AsSpan().SequenceEqual(atApproval.UserKey.EncryptPublicKey))
            {
                await EndPendingLinkAsync(device);
                return Fail("The shared key does not match your account.");
            }

            var current = userKey;
            if (state.UserKey.Generation != generation)
            {
                var currentBox = await FetchOwnBoxAsync(device.DeviceId, state.UserKey.Generation);
                if (!currentBox.Success)
                    return Fail(currentBox.Message);
                current = UserKeyPair.OpenBox(_client.Me.Id, state.UserKey.Generation, device.DeviceId,
                    device.EncryptPrivateKey, currentBox.Data.Box);
                if (!current.EncryptPublicKey.AsSpan().SequenceEqual(state.UserKey.EncryptPublicKey))
                    return Fail("The shared key does not match your account.");
            }

            // Pins go in before the device is ready, so nothing it loads is
            // trusted on first use when the approving device already knew it.
            await ImportLinkPinsAsync(OpenLinkPins(session.Data.ApprovalPins, sessionId, device));

            MyKeyState = state;
            await PinAsync(state, log);
            _pendingDevice = null;
            _pendingSessionId = null;
            _pendingLinkSecret = null;
            await AdoptNewKeysAsync(device, current, newRecovery: null, recoveryToStore: null);
            await RemovePendingLinkAsync(device.DeviceId);
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return Fail(e.Message);
        }
        finally
        {
            _identityLock.Release();
        }

        _ = ServePendingDirectRequestsAsync();
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Finishes a link that was waiting for approval when the app last
    /// stopped. The link is kept while it may still be approved.
    /// </summary>
    private async Task ResumePendingLinkAsync()
    {
        if (_pendingDevice is null)
        {
            var (value, expired) = await LoadPendingAsync(PendingLinkKey);
            if (value is null || expired)
            {
                if (expired)
                    await Store.RemoveAsync(StoreKey(PendingLinkKey));
                return;
            }

            try
            {
                var reader = new E2eeReader(value);
                reader.ReadMagic("VPL1");
                var sessionId = reader.ReadString();
                var secret = reader.ReadBytes();
                var device = DeviceKeyPair.Deserialize(reader.ReadBytes());
                reader.EnsureEnd();

                if (MyKeyState?.EverDevices.ContainsKey(device.DeviceId) == true &&
                    !MyKeyState.ActiveDevices.ContainsKey(device.DeviceId))
                {
                    await Store.RemoveAsync(StoreKey(PendingLinkKey));
                    return;
                }

                _pendingDevice = device;
                _pendingSessionId = sessionId;
                _pendingLinkSecret = secret;
            }
            catch (E2eeFormatException)
            {
                await Store.RemoveAsync(StoreKey(PendingLinkKey));
                return;
            }
        }

        var result = await CompleteLinkAsync();
        if (!result.Success && _pendingDevice is null && result.Message == ApprovalRejectedMessage)
            LinkFailed?.Invoke(result.Message);
    }

    /// <summary>
    /// Remembers a link this device started, in memory and in storage.
    /// </summary>
    private async Task<TaskResult> BeginPendingLinkAsync(DeviceKeyPair device, string sessionId, byte[] secret)
    {
        try
        {
            await Store.SetAsync(StoreKey(PendingLinkKey), WithTimestamp(new E2eeWriter()
                .WriteMagic("VPL1")
                .WriteString(sessionId)
                .WriteBytes(secret)
                .WriteBytes(device.Serialize())
                .ToArray()));
        }
        catch (Exception e)
        {
            LogError("Could not save the new device key", e);
            return Fail("This device's new keys could not be saved, so it cannot be linked. " + e.Message);
        }

        _pendingDevice = device;
        _pendingSessionId = sessionId;
        _pendingLinkSecret = secret;
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Forgets a link this device started, unless another link replaced it.
    /// </summary>
    private async Task EndPendingLinkAsync(DeviceKeyPair device)
    {
        if (_pendingDevice == device)
        {
            _pendingDevice = null;
            _pendingSessionId = null;
            _pendingLinkSecret = null;
        }

        await RemovePendingLinkAsync(device.DeviceId);
    }

    /// <summary>
    /// Removes the stored link if it is for the given device, so another tab
    /// that is linking its own device keeps its link.
    /// </summary>
    private async Task RemovePendingLinkAsync(string deviceId)
    {
        try
        {
            var (value, _) = await LoadPendingAsync(PendingLinkKey);
            if (value is not null)
            {
                var reader = new E2eeReader(value);
                reader.ReadMagic("VPL1");
                reader.ReadString();
                reader.ReadBytes();
                if (TryReadDevice(reader.ReadBytes())?.DeviceId != deviceId)
                    return;
            }

            await Store.RemoveAsync(StoreKey(PendingLinkKey));
        }
        catch (E2eeFormatException)
        {
            await Store.RemoveAsync(StoreKey(PendingLinkKey));
        }
        catch (Exception e)
        {
            LogWarning($"Could not remove a pending device link: {e.Message}");
        }
    }

    /// <summary>
    /// Fetches this account's key log and verifies it. Error is set when the
    /// server could not be reached or the account has no keys.
    /// </summary>
    private async Task<(List<UserKeyLogEntry> Log, UserKeyState State, string Error)> FetchVerifiedOwnLogAsync()
    {
        var log = await FetchOwnLogAsync();
        if (log is null)
            return (null, null, "Could not reach Valour. Try again.");
        if (log.Count == 0)
            return (log, null, "This account has not set up encryption.");
        return (log, UserKeyLogVerifier.Verify(_client.Me.Id, log), null);
    }

    private async Task<TaskResult<UserKeyBoxDto>> FetchOwnBoxAsync(string recipientId, int generation)
    {
        var box = await HubNode.GetJsonAsync<UserKeyBoxDto>(
            $"api/e2ee/users/me/boxes/{Uri.EscapeDataString(recipientId)}?generation={generation}",
            allow404: true, cacheDurationMs: null);
        if (!box.Success)
            return TaskResult<UserKeyBoxDto>.FromFailure("Could not reach Valour. Try again.");
        return box.Data is null
            ? TaskResult<UserKeyBoxDto>.FromFailure("The key for this device could not be found.")
            : TaskResult<UserKeyBoxDto>.FromData(box.Data);
    }

    // Approving a new device (this device is trusted)

    public async Task<List<DeviceLinkSessionDto>> GetPendingLinkRequestsAsync()
    {
        var result = await HubNode.GetJsonAsync<List<DeviceLinkSessionDto>>("api/e2ee/link-sessions/pending",
            cacheDurationMs: null);
        return result.Success ? result.Data ?? [] : [];
    }

    /// <summary>
    /// Shows a code on this device that a new device scans or types to link
    /// itself.
    /// </summary>
    public async Task<TaskResult<DeviceLinkOffer>> ShowLinkCodeAsync()
    {
        if (Status != E2eeStatus.Ready)
            return TaskResult<DeviceLinkOffer>.FromFailure("Set up this device first.");

        var secret = E2eeCrypto.RandomBytes(DeviceLinkCode.ExistingDeviceSecretLength);
        var sessionId = DeviceLinkVerification.SessionIdFor(secret, _client.Me.Id);
        var result = await HubNode.PostAsyncWithResponse<DeviceLinkSessionDto>("api/e2ee/link-sessions",
            new CreateDeviceLinkRequest { Mode = DeviceLinkMode.ExistingDeviceShowsCode, SessionId = sessionId });
        if (!result.Success)
            return TaskResult<DeviceLinkOffer>.FromFailure(result.Message);
        if (result.Data.Id != sessionId)
            return TaskResult<DeviceLinkOffer>.FromFailure("The server did not accept this device's link code.");

        _shownLinkSecrets[sessionId] = secret;
        return TaskResult<DeviceLinkOffer>.FromData(new DeviceLinkOffer
        {
            Session = result.Data,
            QrCode = new DeviceLinkCode(DeviceLinkMode.ExistingDeviceShowsCode, _client.Me.Id, sessionId, null, secret)
                .ToString(),
            TypedCode = DeviceLinkTypedCode.Format(secret)
        });
    }

    /// <summary>
    /// True when this device is showing the link code for the session, so the
    /// screen showing it handles the approval.
    /// </summary>
    public bool IsShowingLinkCode(string sessionId) => _shownLinkSecrets.ContainsKey(sessionId);

    /// <summary>
    /// True when this device started the link session and is waiting for approval.
    /// </summary>
    public bool IsLinkingSession(string sessionId) => _pendingSessionId == sessionId;

    /// <summary>
    /// Checks a QR code scanned from a new device against its request and
    /// keeps the code's secret for the approval. Returns the request the code
    /// belongs to, if any.
    /// </summary>
    public async Task<TaskResult<DeviceLinkSessionDto>> MatchScannedLinkCodeAsync(string scannedCode)
    {
        if (!DeviceLinkCode.TryParse(scannedCode, out var code) || code.Mode != DeviceLinkMode.NewDeviceShowsCode)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("That is not a Valour device link code.");
        if (code.UserId != _client.Me.Id)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("That code belongs to a different account.");

        var session = await HubNode.GetJsonAsync<DeviceLinkSessionDto>(
            $"api/e2ee/link-sessions/{Uri.EscapeDataString(code.SessionId)}", allow404: true, cacheDurationMs: null);
        if (!session.Success || session.Data?.Device is null)
            return TaskResult<DeviceLinkSessionDto>.FromFailure("That link request has expired.");
        if (!DeviceLinkVerification.MatchesFingerprint(session.Data.Device.ToDescriptor().Keys, code.Fingerprint))
            return TaskResult<DeviceLinkSessionDto>.FromFailure(
                "The code does not match the request the server sent. Do not approve this device.");

        _scannedLinkCodes[code.SessionId] = code;
        return session;
    }

    /// <summary>
    /// Approves a new device. A request from a new device showing its code
    /// needs <see cref="MatchScannedLinkCodeAsync"/> first; a request for a
    /// code this device shows needs the new device's proof that it read the
    /// code. Without one of these the server could slip in a device it
    /// controls. The approval carries a proof made with the code's secret,
    /// which the new device checks, and this device's pins, sealed to the new
    /// device.
    /// </summary>
    public async Task<TaskResult> ApproveLinkAsync(DeviceLinkSessionDto session)
    {
        if (Status != E2eeStatus.Ready || Device is null)
            return Fail("Set up this device first.");
        if (session?.Device is null)
            return Fail("The new device has not connected yet.");

        var descriptor = session.Device.ToDescriptor();
        byte[] secret;
        if (session.Mode == DeviceLinkMode.ExistingDeviceShowsCode)
        {
            if (!_shownLinkSecrets.TryGetValue(session.Id, out secret) ||
                !DeviceLinkVerification.VerifyJoinMac(secret, _client.Me.Id, session.Id, descriptor.Keys, session.JoinMac))
                return Fail("The new device did not scan or type this device's code. Do not approve it.");
        }
        else
        {
            if (!_scannedLinkCodes.TryGetValue(session.Id, out var code))
                return Fail("Scan the code on the new device's screen to approve it.");
            if (!DeviceLinkVerification.MatchesFingerprint(descriptor.Keys, code.Fingerprint))
                return Fail("The code does not match the request the server sent. Do not approve this device.");
            secret = code.Secret;
        }

        await _identityLock.WaitAsync();
        try
        {
            var (_, state, error) = await FetchVerifiedOwnLogAsync();
            if (error is not null)
                return Fail(error);

            var userKey = await GetUserKeyAsync(state.UserKey.Generation);
            if (userKey is null)
                return Fail("This device's keys are still loading. Try again in a moment.");

            var entry = UserKeyLogBuilder.AddDevice(state, Device.DeviceId, Device.Sign, descriptor, NowMs());
            var box = UserKeyBox(userKey, descriptor.Keys.DeviceId, descriptor.Keys.EncryptPublicKey);
            var pins = await ExportLinkPinsAsync(session.Id, descriptor.Keys);
            var mac = DeviceLinkVerification.ApprovalMac(secret, _client.Me.Id, session.Id, descriptor.Keys.DeviceId,
                entry.Body, box.Generation, box.Box, pins);

            var result = await HubNode.PostAsyncWithResponse<DeviceLinkSessionDto>(
                $"api/e2ee/link-sessions/{Uri.EscapeDataString(session.Id)}/approve",
                new ApproveDeviceLinkRequest { Entry = entry, Box = box, ApprovalMac = mac, Pins = pins });
            if (!result.Success)
                return Fail(result.Message);

            _shownLinkSecrets.TryRemove(session.Id, out _);
            _scannedLinkCodes.TryRemove(session.Id, out _);

            // The realtime key log event also refreshes the state, so a
            // failure to load it here is not an error.
            var log = await FetchOwnLogAsync();
            if (log is { Count: > 0 })
            {
                MyKeyState = UserKeyLogVerifier.Verify(_client.Me.Id, log);
                await PinAsync(MyKeyState, log);
            }

            return TaskResult.SuccessResult;
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return Fail(e.Message);
        }
        finally
        {
            _identityLock.Release();
        }
    }

    public async Task<TaskResult> DenyLinkAsync(string sessionId)
    {
        _shownLinkSecrets.TryRemove(sessionId, out _);
        _scannedLinkCodes.TryRemove(sessionId, out _);
        return await HubNode.PostAsync($"api/e2ee/link-sessions/{Uri.EscapeDataString(sessionId)}/deny", (object)null);
    }

    // Recovery and reset

    /// <summary>
    /// Restores access on this device with the account's recovery code.
    /// </summary>
    public async Task<TaskResult> RestoreWithRecoveryCodeAsync(string recoveryCode) =>
        (await RestoreCoreAsync(recoveryCode)).Result;

    /// <summary>
    /// Restores this device with a recovery code. Transient is true when the
    /// server could not be reached, so trying again later may work.
    /// </summary>
    private async Task<(TaskResult Result, bool Transient)> RestoreCoreAsync(string recoveryCode)
    {
        if (!RecoveryKey.TryParse(recoveryCode, out var recovery))
            return (Fail("That recovery code is not valid. Check for typos."), false);

        await _identityLock.WaitAsync();
        try
        {
            var (log, state, error) = await FetchVerifiedOwnLogAsync();
            if (error is not null)
                return (Fail(error), log is null);

            if (state.Recovery is null || state.Recovery.DeviceId != recovery.RecoveryId)
                return (Fail("That recovery code does not belong to this account, or it was replaced."), false);

            var box = await HubNode.GetJsonAsync<UserKeyBoxDto>(
                $"api/e2ee/users/me/boxes/{Uri.EscapeDataString(recovery.RecoveryId)}?generation={state.UserKey.Generation}",
                allow404: true, cacheDurationMs: null);
            if (!box.Success)
                return (Fail("Could not reach Valour. Try again."), true);
            if (box.Data is null)
                return (Fail("The recovery key could not be found on the server."), false);

            var userKey = UserKeyPair.OpenBox(_client.Me.Id, state.UserKey.Generation, recovery.RecoveryId,
                recovery.EncryptPrivateKey, box.Data.Box);

            // The server knows the recovery key's public half, so it could
            // seal a key pair of its own choosing. Only the key the log names
            // is accepted.
            if (!userKey.EncryptPublicKey.AsSpan().SequenceEqual(state.UserKey.EncryptPublicKey))
                return (Fail("The recovered key does not match your account."), false);

            var device = DeviceKeyPair.Generate();
            var saved = await TrySavePendingAsync(device, null);
            if (!saved.Success)
                return (saved, false);

            var entry = UserKeyLogBuilder.AddDevice(state, recovery.RecoveryId, recovery.Sign,
                UserKeyLogBuilder.Describe(_client.Me.Id, device, DeviceName), NowMs());
            var (result, uncertain) = await AppendOwnLogAsync(entry,
                [UserKeyBox(userKey, device.DeviceId, device.EncryptPublicKey)]);
            if (!result.Success)
            {
                await AbandonPendingKeysAsync(device.DeviceId, recovery: false, uncertain);
                return (Fail(result.Message), uncertain);
            }

            await AdoptNewKeysAsync(device, userKey, newRecovery: null,
                recoveryToStore: StoreRecoveryCode ? recovery : null);
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return (Fail(e.Message), false);
        }
        finally
        {
            _identityLock.Release();
        }

        _ = ServePendingDirectRequestsAsync();
        return (TaskResult.SuccessResult, false);
    }

    /// <summary>
    /// Replaces the recovery code. The old code stops working. Returns the new
    /// code, which the person must save.
    /// </summary>
    public async Task<TaskResult<string>> CreateNewRecoveryCodeAsync()
    {
        if (Status != E2eeStatus.Ready)
            return TaskResult<string>.FromFailure("Set up this device first.");

        await _identityLock.WaitAsync();
        try
        {
            var log = await FetchOwnLogAsync();
            if (log is null)
                return TaskResult<string>.FromFailure("Could not reach Valour. Try again.");

            var state = UserKeyLogVerifier.Verify(_client.Me.Id, log);
            var recovery = RecoveryKey.Generate();
            var saved = await TrySavePendingAsync(null, recovery);
            if (!saved.Success)
                return TaskResult<string>.FromFailure(saved.Message);

            var entry = UserKeyLogBuilder.SetRecovery(state, Device.DeviceId, Device.Sign, recovery, NowMs());
            var (result, uncertain) = await AppendOwnLogAsync(entry,
                [UserKeyBox(CurrentUserKey, recovery.RecoveryId, recovery.EncryptPublicKey)]);
            if (!result.Success)
            {
                // A code the server may have accepted is finished by the next
                // start; this device stays ready meanwhile.
                if (!uncertain)
                    await ClearPendingRecoveryAsync();
                return TaskResult<string>.FromFailure(result.Message);
            }

            await MarkRecoveryCodeUnsavedAsync(recovery);
            try
            {
                if (StoreRecoveryCode)
                    await Store.SetAsync(StoreKey(RecoveryCodeKey), Encoding.UTF8.GetBytes(recovery.ToCode()));
                await ClearPendingRecoveryAsync();
            }
            catch (Exception e)
            {
                LogError("Could not store the new recovery code on this device", e);
            }

            return TaskResult<string>.FromData(recovery.ToCode());
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return TaskResult<string>.FromFailure(e.Message);
        }
        finally
        {
            _identityLock.Release();
        }
    }

    /// <summary>
    /// Starts over with new keys when every device and the recovery code are
    /// lost. Encrypted messages from before cannot be read afterwards, and
    /// other people see that this account's keys changed. Returns the new
    /// recovery code.
    /// </summary>
    public async Task<TaskResult<string>> ResetKeysAsync() => (await ResetCoreAsync()).Result;

    /// <summary>
    /// Resets this account's keys. Transient is true when the server could
    /// not be reached, so the reset may or may not have happened; the next
    /// start finds out.
    /// </summary>
    private async Task<(TaskResult<string> Result, bool Transient)> ResetCoreAsync()
    {
        await _identityLock.WaitAsync();
        try
        {
            var log = await FetchOwnLogAsync();
            if (log is null)
                return (TaskResult<string>.FromFailure("Could not reach Valour. Try again."), true);

            var device = DeviceKeyPair.Generate();
            var recovery = RecoveryKey.Generate();
            UserKeyLogEntry entry;
            UserKeyPair userKey;

            if (log.Count == 0)
            {
                userKey = UserKeyPair.Generate(1);
                entry = UserKeyLogBuilder.Genesis(_client.Me.Id, device, DeviceName, userKey, recovery, NowMs());
            }
            else
            {
                var state = UserKeyLogVerifier.Verify(_client.Me.Id, log);
                userKey = UserKeyPair.Generate(state.UserKeyGenerations.Keys.Max() + 1);
                entry = UserKeyLogBuilder.Reset(state, device, DeviceName, userKey, recovery, NowMs());
            }

            var saved = await TrySavePendingAsync(device, recovery);
            if (!saved.Success)
                return (TaskResult<string>.FromFailure(saved.Message), false);

            var (result, uncertain) = await AppendOwnLogAsync(entry,
            [
                UserKeyBox(userKey, device.DeviceId, device.EncryptPublicKey),
                UserKeyBox(userKey, recovery.RecoveryId, recovery.EncryptPublicKey)
            ]);
            if (!result.Success)
            {
                await AbandonPendingKeysAsync(device.DeviceId, recovery: true, uncertain);
                return (TaskResult<string>.FromFailure(result.Message), uncertain);
            }

            // Keys of earlier generations stay in storage, so messages this
            // device could read before stay readable to it.
            _keyRings.Clear();
            _userKeys.Clear();
            await AdoptNewKeysAsync(device, userKey, newRecovery: recovery, recoveryToStore: recovery);
            return (TaskResult<string>.FromData(recovery.ToCode()), false);
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return (TaskResult<string>.FromFailure(e.Message), false);
        }
        finally
        {
            _identityLock.Release();
        }
    }

    /// <summary>
    /// Removes a device from the account. It keeps what it already
    /// downloaded, but cannot read anything encrypted with the new user key.
    /// Removing this device signs it out of encryption.
    /// </summary>
    public async Task<TaskResult> RevokeDeviceAsync(string deviceId)
    {
        if (Status != E2eeStatus.Ready || Device is null)
            return Fail("Set up this device first.");

        await _identityLock.WaitAsync();
        try
        {
            var log = await FetchOwnLogAsync();
            if (log is null)
                return Fail("Could not reach Valour. Try again.");

            var state = UserKeyLogVerifier.Verify(_client.Me.Id, log);
            if (!state.ActiveDevices.ContainsKey(deviceId))
                return Fail("That device is not active.");

            var current = await GetUserKeyAsync(state.UserKey.Generation);
            var next = UserKeyPair.Generate(state.UserKey.Generation + 1);
            var entry = UserKeyLogBuilder.RevokeDevice(state, Device.DeviceId, Device.Sign, deviceId, next, current, NowMs());

            var boxes = state.ActiveDevices.Values
                .Where(d => d.DeviceId != deviceId)
                .Select(d => UserKeyBox(next, d.DeviceId, d.Keys.EncryptPublicKey))
                .ToList();
            if (state.Recovery is not null)
                boxes.Add(UserKeyBox(next, state.Recovery.DeviceId, state.Recovery.EncryptPublicKey));

            var (result, _) = await AppendOwnLogAsync(entry, boxes);
            if (!result.Success)
                return Fail(result.Message);

            if (deviceId == Device.DeviceId)
            {
                await ForgetLocalKeysAsync();
                SetStatus(E2eeStatus.NeedsVerification);
            }
            else
            {
                await StoreUserKeyAsync(next);
            }

            return TaskResult.SuccessResult;
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            return Fail(e.Message);
        }
        finally
        {
            _identityLock.Release();
        }
    }

    /// <summary>
    /// Signs this device out of encryption when the person logs out: removes
    /// it from the account and deletes its keys. If it was the last device,
    /// the recovery code restores access on the next login.
    /// </summary>
    public async Task SignOutDeviceAsync()
    {
        if (Status == E2eeStatus.Ready && Device is not null)
        {
            var result = await RevokeDeviceAsync(Device.DeviceId);
            if (result.Success)
            {
                Reset();
                return;
            }
        }

        if (_client.Me is not null && Device is not null)
        {
            // Another tab may have stored a newer device for the account;
            // only keys this device owns are removed.
            var stored = TryReadDevice(await Store.GetAsync(StoreKey(DeviceKey)) ?? []);
            var ownsStorage = stored is null || stored.DeviceId == Device.DeviceId;
            await ForgetLocalKeysAsync();
            if (ownsStorage)
            {
                await Store.RemoveAsync(StoreKey(RecoveryCodeKey));
                await Store.RemoveAsync(StoreKey(RecoveryUnsavedKey));
            }
        }

        Reset();
    }
}
