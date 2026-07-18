using System.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Client.Components.Utility;
using Valour.Client.Device;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Client.Components.Calls;

public sealed class GlobalCallSessionService : IAsyncDisposable
{
    private readonly ValourClient _client;
    private readonly RealtimeKitHostService _rtkHost;
    private readonly SemaphoreSlim _joinLock = new(1, 1);

    private const int MinimumRealtimeKitParticipants = 2;
    private static readonly TimeSpan TokenRequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan PermissionRequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ResetTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ParticipantSnapshotTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StopParticipantRefreshWaitTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan LeaveTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RtkHostReadyTimeout = TimeSpan.FromSeconds(2);

    private CancellationTokenSource? _participantRefreshLoopCts;
    private Task? _participantRefreshLoopTask;
    private CancellationTokenSource? _heartbeatLoopCts;
    private Task? _heartbeatLoopTask;
    private int _participantRefreshInProgress;
    private IDisposable? _voiceSessionReplaceSubscription;
    private IDisposable? _voiceModerationSubscription;

    private readonly HashSet<long> _moderatorMutedParticipantUserIds = new();
    private readonly string _voiceSessionId = Guid.NewGuid().ToString("N");

    // Planets whose community-hosted-voice warning the user accepted this session.
    private readonly HashSet<long> _acknowledgedVoicePlanets = new();
    private (Channel Channel, bool VideoMode)? _pendingCommunityVoiceJoin;

    private bool _registeredWithVoiceState;
    private bool _disposed;

    public event Action? StateChanged;

    public GlobalCallSessionService(ValourClient client, RealtimeKitHostService rtkHost)
    {
        _client = client;
        _rtkHost = rtkHost;

        DevicePreferences.OnMicrophoneDeviceIdChanged += OnMicrophoneSelected;
        DevicePreferences.OnCameraDeviceIdChanged += OnCameraSelected;
        BrowserUtils.Focused += OnAppResumed;
        _client.VoiceStateService.VoiceParticipantsChanged += OnVoiceParticipantsChanged;
    }

    public Channel? ActiveChannel { get; private set; }
    public bool VideoMode { get; private set; }
    public bool Joined { get; private set; }
    public bool Connecting { get; private set; }
    public bool WaitingForPeer { get; private set; }
    public bool AudioEnabled { get; private set; } = true;
    public bool VideoEnabled { get; private set; }
    public bool ScreenShareEnabled { get; private set; }
    public string? Error { get; private set; }
    public bool CanMuteParticipants { get; private set; }
    public bool CanKickParticipants { get; private set; }

    /// <summary>
    /// True when the join is paused on the community-hosted-voice warning —
    /// the user must accept (AcknowledgeCommunityVoiceAsync) or cancel.
    /// </summary>
    public bool PendingCommunityVoiceAck { get; private set; }

    /// <summary>
    /// Host of the community SFU carrying the active (or pending) call, when the
    /// call runs on planet-owned infrastructure. Null on the instance backend.
    /// </summary>
    public string? CommunityVoiceHost { get; private set; }
    public RealtimeKitParticipantsSnapshot? ParticipantsSnapshot { get; private set; }
    public long ParticipantsVersion { get; private set; }
    public IReadOnlyCollection<long> ModeratorMutedParticipantUserIds => _moderatorMutedParticipantUserIds;
    public string VoiceSessionId => _voiceSessionId;

    private RealtimeKitComponent? Rtk => _rtkHost.Component;

    /// <summary>
    /// Accepts the community-hosted-voice warning for the pending planet and
    /// resumes the interrupted join. Accepted planets don't warn again this session.
    /// </summary>
    public async Task AcknowledgeCommunityVoiceAsync()
    {
        if (!PendingCommunityVoiceAck || _pendingCommunityVoiceJoin is null)
            return;

        var (channel, videoMode) = _pendingCommunityVoiceJoin.Value;
        if (channel.PlanetId is not null)
            _acknowledgedVoicePlanets.Add(channel.PlanetId.Value);

        PendingCommunityVoiceAck = false;
        _pendingCommunityVoiceJoin = null;

        await InitializeAsync(channel, videoMode);
    }

    /// <summary>Dismisses the community-hosted-voice warning without joining.</summary>
    public void CancelCommunityVoiceJoin()
    {
        PendingCommunityVoiceAck = false;
        _pendingCommunityVoiceJoin = null;
        NotifyStateChanged();
    }

    public async Task InitializeAsync(Channel channel, bool videoMode)
    {
        if (_disposed)
            return;

        SubscribeToVoiceHubEvents();

        if (!ISharedChannel.VoiceChannelTypes.Contains(channel.ChannelType))
            return;

        // Community-hosted voice: pause on an explicit warning before the first
        // join of a planet whose calls run on its own SFU. The user's IP (and the
        // call itself) go to that operator's server, not Valour.
        var planet = channel.PlanetId is null ? null : channel.Planet;
        if (planet?.SelfHostedVoice == true && !_acknowledgedVoicePlanets.Contains(planet.Id))
        {
            _pendingCommunityVoiceJoin = (channel, videoMode);
            PendingCommunityVoiceAck = true;
            NotifyStateChanged();
            return;
        }

        if (Rtk is null)
        {
            await _rtkHost.WaitForComponentAsync(RtkHostReadyTimeout);
        }

        if (Rtk is null)
        {
            Error = "Voice system is still loading. Try again.";
            NotifyStateChanged();
            return;
        }

        await _joinLock.WaitAsync();

        try
        {
            var previousActiveChannel = ActiveChannel;
            if (Joined ||
                (_registeredWithVoiceState &&
                 previousActiveChannel is not null &&
                 previousActiveChannel.Id != channel.Id))
            {
                await LeaveAsync(clearChannel: false);
            }

            Error = null;
            ActiveChannel = channel;
            VideoMode = videoMode;
            await RefreshModerationPermissionsAsync(channel);
            Connecting = true;
            WaitingForPeer = false;
            NotifyStateChanged();

            if (_client.PrimaryNode is null)
            {
                Error = "Voice connection is unavailable.";
                Connecting = false;
                NotifyStateChanged();
                return;
            }

            // Once the token request is in flight, the server may have registered us
            // in Valour voice state even if the HTTP response times out locally.
            _registeredWithVoiceState = true;

            var tokenResult = await _client.PrimaryNode.PostAsyncWithResponse<RealtimeKitVoiceTokenResponse>(
                    $"api/voice/token/{channel.Id}?sessionId={Uri.EscapeDataString(_voiceSessionId)}")
                .WaitAsync(TokenRequestTimeout);

            if (!tokenResult.Success || tokenResult.Data is null)
            {
                await LeaveAsync(clearChannel: false);
                Error = string.IsNullOrWhiteSpace(tokenResult.Message)
                    ? "Failed to fetch a voice token from the server."
                    : tokenResult.Message;
                Connecting = false;
                NotifyStateChanged();
                return;
            }

            if (tokenResult.Data.WaitingForPeer)
            {
                WaitingForPeer = true;
                Connecting = false;
                Error = null;
                StartHeartbeatLoop();
                NotifyStateChanged();
                return;
            }

            if (string.IsNullOrWhiteSpace(tokenResult.Data.AuthToken))
            {
                await LeaveAsync(clearChannel: false);
                Error = "Failed to fetch a voice token from the server.";
                Connecting = false;
                NotifyStateChanged();
                return;
            }

            var rtk = Rtk;
            if (rtk is null)
            {
                await LeaveAsync(clearChannel: false);
                Error = "Voice system is still loading. Try again.";
                Connecting = false;
                NotifyStateChanged();
                return;
            }

            // Surface the community host in the call UI when the planet brings its
            // own SFU (the token's Url is that server).
            CommunityVoiceHost = tokenResult.Data.SelfHosted &&
                                 Uri.TryCreate(tokenResult.Data.Url, UriKind.Absolute, out var sfuUri)
                ? sfuUri.Host
                : null;

            await rtk.InitializeAsync(BuildInitOptions(tokenResult.Data, videoMode), tokenResult.Data.Provider);
            await rtk.JoinRoomAsync((int)JoinTimeout.TotalMilliseconds);

            Joined = true;
            Connecting = false;
            WaitingForPeer = false;
            AppLifecycle.NotifyCallStarted();

            try
            {
                if (await EnsureMicrophonePermissionAsync())
                {
                    await rtk.EnableAudioAsync();
                    await SetMicAsync(DevicePreferences.MicrophoneDeviceId);
                }
                else
                {
                    AudioEnabled = false;
                }
            }
            catch
            {
                AudioEnabled = false;
            }

            await RefreshStateFromSdkAsync();
            await RefreshParticipantsAsync();
            StartParticipantRefreshLoop();
            StartHeartbeatLoop();
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            Error = GetExceptionMessage(ex, "Failed to connect to voice.");
            Connecting = false;
            await LeaveAsync(clearChannel: false);
            NotifyStateChanged();
        }
        finally
        {
            _joinLock.Release();
        }
    }

    public async Task ReconnectAsync()
    {
        var channel = ActiveChannel;
        if (channel is null)
            return;

        await InitializeAsync(channel, VideoMode);
    }

    public async Task ToggleAudioAsync()
    {
        var rtk = Rtk;
        if (rtk is null || !Joined)
            return;

        try
        {
            if (AudioEnabled)
            {
                await rtk.DisableAudioAsync();
            }
            else
            {
                if (!await EnsureMicrophonePermissionAsync())
                {
                    NotifyStateChanged();
                    return;
                }

                await rtk.EnableAudioAsync();
                await SetMicAsync(DevicePreferences.MicrophoneDeviceId);
            }

            await RefreshStateFromSdkAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        NotifyStateChanged();
    }

    public async Task ToggleVideoAsync()
    {
        var rtk = Rtk;
        if (rtk is null || !Joined)
            return;

        try
        {
            if (VideoEnabled)
            {
                await rtk.DisableVideoAsync();
            }
            else
            {
                if (!await EnsureCameraPermissionAsync())
                {
                    NotifyStateChanged();
                    return;
                }

                await rtk.EnableVideoAsync();
                await SetCameraAsync(DevicePreferences.CameraDeviceId);
            }

            await RefreshStateFromSdkAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        NotifyStateChanged();
    }

    public async Task ToggleScreenShareAsync()
    {
        var rtk = Rtk;
        if (rtk is null || !Joined || !VideoMode)
            return;

        try
        {
            if (ScreenShareEnabled)
            {
                await rtk.DisableScreenShareAsync();
            }
            else
            {
                if (!await rtk.IsScreenShareSupportedAsync())
                {
                    Error = "Screen sharing is not supported on this browser/device.";
                    NotifyStateChanged();
                    return;
                }

                await rtk.EnableScreenShareAsync();
            }

            await RefreshStateFromSdkAsync();
            await RefreshParticipantsAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        NotifyStateChanged();
    }

    public async Task SendModerationActionAsync(long targetUserId, bool isSelf, string action)
    {
        if (ActiveChannel is null || isSelf || targetUserId <= 0 || _client.PrimaryNode is null)
            return;

        if (action == "mute" && !CanMuteParticipants)
            return;

        if (action == "kick" && !CanKickParticipants)
            return;

        var result = await _client.PrimaryNode.PostAsync(
            $"api/voice/channels/{ActiveChannel.Id}/participants/{targetUserId}/{action}",
            new { });

        if (!result.Success)
        {
            Error = string.IsNullOrWhiteSpace(result.Message)
                ? $"Failed to {action} participant."
                : result.Message;
            NotifyStateChanged();
            return;
        }

        if (action == "mute")
            _moderatorMutedParticipantUserIds.Add(targetUserId);
        else if (action is "unmute" or "kick")
            _moderatorMutedParticipantUserIds.Remove(targetUserId);

        Error = null;
        NotifyStateChanged();
    }

    public async Task LeaveAsync(bool clearChannel)
    {
        var leaveChannelId = ActiveChannel?.Id;
        var wasJoined = Joined;
        var wasRegisteredWithVoiceState = _registeredWithVoiceState || WaitingForPeer;

        if ((wasJoined || wasRegisteredWithVoiceState) &&
            leaveChannelId is not null &&
            _client.PrimaryNode is not null)
        {
            try
            {
                await _client.PrimaryNode.PostAsync(
                    $"api/voice/channels/{leaveChannelId}/leave?sessionId={Uri.EscapeDataString(_voiceSessionId)}",
                    new { })
                    .WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Ignore leave notification failures during teardown.
            }
        }

        var rtk = Rtk;
        if (rtk is not null)
        {
            try
            {
                if (wasJoined)
                    await rtk.LeaveRoomAsync().WaitAsync(LeaveTimeout);
                else
                    await rtk.ResetAsync().WaitAsync(ResetTimeout);
            }
            catch
            {
                // Ignore leave failures during teardown.
            }
        }

        await StopHeartbeatLoopAsync();
        await StopParticipantRefreshLoopAsync();

        Joined = false;
        Connecting = false;
        WaitingForPeer = false;
        _registeredWithVoiceState = false;
        AudioEnabled = true;
        VideoEnabled = false;
        ScreenShareEnabled = false;
        CanMuteParticipants = false;
        CanKickParticipants = false;
        CommunityVoiceHost = null;
        _moderatorMutedParticipantUserIds.Clear();

        if (ParticipantsSnapshot is not null)
        {
            ParticipantsSnapshot = null;
            ParticipantsVersion++;
        }

        AppLifecycle.NotifyCallEnded();

        if (clearChannel)
        {
            ActiveChannel = null;
            Error = null;
            VideoMode = false;
        }

        NotifyStateChanged();
    }

    public void ClearError()
    {
        if (string.IsNullOrEmpty(Error))
            return;

        Error = null;
        NotifyStateChanged();
    }

    public async Task SetMicAsync(string? deviceId)
    {
        var rtk = Rtk;
        if (rtk is null || !Joined || string.IsNullOrWhiteSpace(deviceId))
            return;

        try
        {
            await rtk.SetDeviceAsync(new RealtimeKitDeviceSelection
            {
                DeviceId = deviceId,
                Kind = "audioinput"
            });

            await RefreshStateFromSdkAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        NotifyStateChanged();
    }

    public async Task SetCameraAsync(string? deviceId)
    {
        var rtk = Rtk;
        if (rtk is null || !Joined || string.IsNullOrWhiteSpace(deviceId))
            return;

        try
        {
            await rtk.SetDeviceAsync(new RealtimeKitDeviceSelection
            {
                DeviceId = deviceId,
                Kind = "videoinput"
            });

            await RefreshStateFromSdkAsync();
            await RefreshParticipantsAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        NotifyStateChanged();
    }

    private void SubscribeToVoiceHubEvents()
    {
        var hub = _client.PrimaryNode?.HubConnection;
        if (hub is null)
            return;

        _voiceSessionReplaceSubscription?.Dispose();
        _voiceSessionReplaceSubscription = hub.On<VoiceSessionReplaceEvent>(
            "Voice-Session-Replace",
            OnVoiceSessionReplace);

        _voiceModerationSubscription?.Dispose();
        _voiceModerationSubscription = hub.On<VoiceModerationEvent>(
            "Voice-Moderation-Action",
            OnVoiceModerationAction);
    }

    private async Task RefreshStateFromSdkAsync()
    {
        var rtk = Rtk;
        if (rtk is null || !Joined)
            return;

        var state = await rtk.GetSelfStateAsync();
        if (state is null)
            return;

        AudioEnabled = state.AudioEnabled;
        VideoEnabled = state.VideoEnabled;
        ScreenShareEnabled = state.ScreenShareEnabled;
    }

    private async Task RefreshModerationPermissionsAsync(Channel channel)
    {
        CanMuteParticipants = false;
        CanKickParticipants = false;

        if (channel.PlanetId is null)
            return;

        try
        {
            var muteTask = channel
                .HasPermissionAsync(_client.Me.Id, VoiceChannelPermissions.MuteMembers)
                .WaitAsync(PermissionRequestTimeout);
            var kickTask = channel
                .HasPermissionAsync(_client.Me.Id, VoiceChannelPermissions.KickMembers)
                .WaitAsync(PermissionRequestTimeout);

            await Task.WhenAll(muteTask, kickTask);
            CanMuteParticipants = muteTask.Result;
            CanKickParticipants = kickTask.Result;
        }
        catch
        {
            CanMuteParticipants = false;
            CanKickParticipants = false;
        }
    }

    private async Task<bool> EnsureMicrophonePermissionAsync()
    {
        var rtk = Rtk;
        if (rtk is null)
            return false;

        try
        {
            var permissionState = await rtk.GetMicrophonePermissionStateAsync();
            if (string.Equals(permissionState, "denied", StringComparison.OrdinalIgnoreCase))
            {
                Error = "Microphone access is blocked. Enable it in browser site settings.";
                return false;
            }

            if (string.Equals(permissionState, "unsupported", StringComparison.OrdinalIgnoreCase))
            {
                Error = "Microphone access is not supported on this browser/device.";
                return false;
            }

            if (string.Equals(permissionState, "granted", StringComparison.OrdinalIgnoreCase))
                return true;

            var granted = await rtk.RequestMicrophonePermissionAsync();
            Error = granted ? null : "Microphone permission was not granted.";
            return granted;
        }
        catch
        {
            return true;
        }
    }

    private async Task<bool> EnsureCameraPermissionAsync()
    {
        var rtk = Rtk;
        if (rtk is null)
            return false;

        try
        {
            var permissionState = await rtk.GetCameraPermissionStateAsync();
            if (string.Equals(permissionState, "denied", StringComparison.OrdinalIgnoreCase))
            {
                Error = "Camera access is blocked. Enable it in browser site settings.";
                return false;
            }

            if (string.Equals(permissionState, "unsupported", StringComparison.OrdinalIgnoreCase))
            {
                Error = "Camera access is not supported on this browser/device.";
                return false;
            }

            if (string.Equals(permissionState, "granted", StringComparison.OrdinalIgnoreCase))
                return true;

            var granted = await rtk.RequestPlatformVideoPermissionAsync();
            Error = granted ? null : "Camera permission was not granted.";
            return granted;
        }
        catch
        {
            return true;
        }
    }

    private async Task RefreshParticipantsAsync()
    {
        var rtk = Rtk;
        if (rtk is null || !Joined)
            return;

        if (Interlocked.Exchange(ref _participantRefreshInProgress, 1) == 1)
            return;

        try
        {
            var snapshot = await rtk.GetParticipantsSnapshotAsync().WaitAsync(ParticipantSnapshotTimeout);
            if (AreParticipantSnapshotsEqual(ParticipantsSnapshot, snapshot))
                return;

            ParticipantsSnapshot = snapshot;
            ParticipantsVersion++;
            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            Error = GetExceptionMessage(ex, "Failed to refresh call participants.");
            NotifyStateChanged();
        }
        finally
        {
            Interlocked.Exchange(ref _participantRefreshInProgress, 0);
        }
    }

    private void StartParticipantRefreshLoop()
    {
        if (_participantRefreshLoopTask is { IsCompleted: false })
            return;

        _participantRefreshLoopCts?.Dispose();
        _participantRefreshLoopCts = new CancellationTokenSource();
        var cancellationToken = _participantRefreshLoopCts.Token;

        _participantRefreshLoopTask = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RefreshParticipantsAsync();
                    await Task.Delay(350, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Keep the participant refresh loop alive across transient SDK failures.
                }
            }
        }, cancellationToken);
    }

    private async Task StopParticipantRefreshLoopAsync()
    {
        if (_participantRefreshLoopCts is null)
            return;

        var cts = _participantRefreshLoopCts;
        var refreshTask = _participantRefreshLoopTask;

        _participantRefreshLoopCts = null;
        _participantRefreshLoopTask = null;

        try
        {
            cts.Cancel();
            if (refreshTask is not null)
            {
                await refreshTask.WaitAsync(StopParticipantRefreshWaitTimeout);
            }
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        finally
        {
            cts.Dispose();
        }
    }

    private void StartHeartbeatLoop()
    {
        if (_heartbeatLoopTask is { IsCompleted: false })
            return;

        _heartbeatLoopCts?.Dispose();
        _heartbeatLoopCts = new CancellationTokenSource();
        var cancellationToken = _heartbeatLoopCts.Token;

        _heartbeatLoopTask = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(45), cancellationToken);
                    if (_client.PrimaryNode is not null)
                    {
                        await _client.PrimaryNode.PostAsync("api/voice/heartbeat", new { });
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Keep heartbeat loop alive across transient failures.
                }
            }
        }, cancellationToken);
    }

    private async Task StopHeartbeatLoopAsync()
    {
        if (_heartbeatLoopCts is null)
            return;

        var cts = _heartbeatLoopCts;
        var heartbeatTask = _heartbeatLoopTask;

        _heartbeatLoopCts = null;
        _heartbeatLoopTask = null;

        try
        {
            cts.Cancel();
            if (heartbeatTask is not null)
            {
                await heartbeatTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task OnMicrophoneSelected(string? id)
    {
        try
        {
            await SetMicAsync(id);
        }
        catch
        {
            // Ignore device change failures.
        }
    }

    private async Task OnCameraSelected(string? id)
    {
        try
        {
            await SetCameraAsync(id);
        }
        catch
        {
            // Ignore device change failures.
        }
    }

    private async Task OnAppResumed()
    {
        if (WaitingForPeer && ActiveChannel is not null)
        {
            var participantCount = _client.VoiceStateService.GetParticipantCount(ActiveChannel.Id);
            if (participantCount >= MinimumRealtimeKitParticipants)
            {
                await InitializeAsync(ActiveChannel, VideoMode);
            }

            return;
        }

        if (!Joined || ActiveChannel is null || Rtk is null)
            return;

        try
        {
            await Rtk.GetSelfStateAsync();
        }
        catch
        {
            var channel = ActiveChannel;
            if (channel is not null)
            {
                await InitializeAsync(channel, VideoMode);
            }
        }
    }

    private void OnVoiceSessionReplace(VoiceSessionReplaceEvent update)
    {
        if (update is null)
            return;

        if (string.Equals(update.SessionId, _voiceSessionId, StringComparison.Ordinal))
            return;

        _ = HandleVoiceSessionReplaceAsync(update);
    }

    private async Task HandleVoiceSessionReplaceAsync(VoiceSessionReplaceEvent update)
    {
        if (ActiveChannel is null || ActiveChannel.Id != update.ChannelId)
            return;

        if (!Joined && !Connecting && !WaitingForPeer)
            return;

        Error = "Disconnected because this account joined this call from another instance.";
        await LeaveAsync(clearChannel: false);
        NotifyStateChanged();
    }

    private void OnVoiceModerationAction(VoiceModerationEvent update)
    {
        if (update is null)
            return;

        _ = HandleVoiceModerationActionAsync(update);
    }

    private async Task HandleVoiceModerationActionAsync(VoiceModerationEvent update)
    {
        if (ActiveChannel is null || ActiveChannel.Id != update.ChannelId)
            return;

        if (!Joined && !Connecting)
            return;

        var rtk = Rtk;
        switch (update.Action)
        {
            case VoiceModerationActionType.Mute:
                if (rtk is not null && Joined && AudioEnabled)
                {
                    try
                    {
                        await rtk.DisableAudioAsync();
                        await RefreshStateFromSdkAsync();
                    }
                    catch { }
                }
                break;
            case VoiceModerationActionType.Unmute:
                if (rtk is not null && Joined && !AudioEnabled)
                {
                    try
                    {
                        await rtk.EnableAudioAsync();
                        await SetMicAsync(DevicePreferences.MicrophoneDeviceId);
                        await RefreshStateFromSdkAsync();
                    }
                    catch { }
                }
                break;
            case VoiceModerationActionType.Kick:
                Error = "You were removed from this call.";
                await LeaveAsync(clearChannel: false);
                break;
        }

        NotifyStateChanged();
    }

    private void OnVoiceParticipantsChanged(long channelId)
    {
        if (ActiveChannel?.Id != channelId)
            return;

        _ = HandleVoiceParticipantsChangedAsync(channelId);
    }

    private async Task HandleVoiceParticipantsChangedAsync(long channelId)
    {
        var channel = ActiveChannel;
        if (channel is null || channel.Id != channelId || _disposed)
            return;

        var participantCount = _client.VoiceStateService.GetParticipantCount(channelId);

        if (WaitingForPeer &&
            !Connecting &&
            !Joined &&
            participantCount >= MinimumRealtimeKitParticipants)
        {
            await InitializeAsync(channel, VideoMode);
            return;
        }

        if (Joined && participantCount < MinimumRealtimeKitParticipants)
        {
            await SuspendRealtimeKitUntilPeerJoinsAsync(channelId);
        }
    }

    private async Task SuspendRealtimeKitUntilPeerJoinsAsync(long channelId)
    {
        await _joinLock.WaitAsync();

        try
        {
            if (!Joined || ActiveChannel is null || ActiveChannel.Id != channelId)
                return;

            var participantCount = _client.VoiceStateService.GetParticipantCount(channelId);
            if (participantCount >= MinimumRealtimeKitParticipants)
                return;

            var rtk = Rtk;
            if (rtk is not null)
            {
                try
                {
                    await rtk.LeaveRoomAsync().WaitAsync(LeaveTimeout);
                }
                catch
                {
                    // RTK may already have been kicked by the server-side backstop.
                }
            }

            await StopParticipantRefreshLoopAsync();

            Joined = false;
            Connecting = false;
            WaitingForPeer = true;
            _registeredWithVoiceState = true;
            AudioEnabled = true;
            VideoEnabled = false;
            ScreenShareEnabled = false;
            CanMuteParticipants = false;
            CanKickParticipants = false;
            _moderatorMutedParticipantUserIds.Clear();
            Error = null;

            if (ParticipantsSnapshot is not null)
            {
                ParticipantsSnapshot = null;
                ParticipantsVersion++;
            }

            AppLifecycle.NotifyCallEnded();
            NotifyStateChanged();
        }
        finally
        {
            _joinLock.Release();
        }
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

    private static RealtimeKitInitOptions BuildInitOptions(RealtimeKitVoiceTokenResponse token, bool videoMode)
    {
        // For LiveKit, BaseUri carries the SFU websocket URL the client connects to
        // (its interop reads options.baseURI); for RealtimeKit it's the Cloudflare host.
        var isLiveKit = string.Equals(token.Provider, "livekit", StringComparison.OrdinalIgnoreCase);

        return new RealtimeKitInitOptions
        {
            AuthToken = token.AuthToken,
            BaseUri = isLiveKit ? token.Url : "realtime.cloudflare.com",
            Defaults = new RealtimeKitMediaDefaults
            {
                Audio = false,
                Video = false,
                MediaConfiguration = new RealtimeKitMediaConfiguration
                {
                    Audio = new RealtimeKitAudioMediaConfiguration
                    {
                        EnableHighBitrate = true,
                        EnableStereo = true
                    },
                    Video = new RealtimeKitVideoMediaConfiguration
                    {
                        Width = new RealtimeKitNumericConstraint { Ideal = 1920 },
                        Height = new RealtimeKitNumericConstraint { Ideal = 1080 },
                        FrameRate = new RealtimeKitNumericConstraint { Ideal = 60, Max = 60 }
                    }
                }
            },
            Overrides = new RealtimeKitOverrides
            {
                SimulcastConfig = new RealtimeKitSimulcastConfig
                {
                    Disable = false,
                    Encodings = videoMode
                        ? new[]
                        {
                            new RealtimeKitSimulcastEncoding
                            {
                                Rid = "q",
                                ScaleResolutionDownBy = 4,
                                MaxBitrate = 200000,
                                MaxFramerate = 15,
                                ScalabilityMode = "L1T1"
                            },
                            new RealtimeKitSimulcastEncoding
                            {
                                Rid = "h",
                                ScaleResolutionDownBy = 2,
                                MaxBitrate = 800000,
                                MaxFramerate = 30,
                                ScalabilityMode = "L1T1"
                            },
                            new RealtimeKitSimulcastEncoding
                            {
                                Rid = "f",
                                MaxBitrate = 2500000,
                                MaxFramerate = 60,
                                ScalabilityMode = "L1T1"
                            }
                        }
                        : null
                }
            }
        };
    }

    private static bool AreParticipantSnapshotsEqual(
        RealtimeKitParticipantsSnapshot? left,
        RealtimeKitParticipantsSnapshot? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        if (!string.Equals(left.ActiveSpeakerPeerId, right.ActiveSpeakerPeerId, StringComparison.Ordinal))
            return false;

        var lp = left.Participants ?? Array.Empty<RealtimeKitParticipantState>();
        var rp = right.Participants ?? Array.Empty<RealtimeKitParticipantState>();
        if (lp.Length != rp.Length)
            return false;

        for (int i = 0; i < lp.Length; i++)
        {
            var a = lp[i];
            var b = rp[i];

            if (!string.Equals(a.PeerId, b.PeerId, StringComparison.Ordinal) ||
                !string.Equals(a.UserId, b.UserId, StringComparison.Ordinal) ||
                !string.Equals(a.CustomParticipantId, b.CustomParticipantId, StringComparison.Ordinal) ||
                !string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                a.AudioEnabled != b.AudioEnabled ||
                a.VideoEnabled != b.VideoEnabled ||
                a.ScreenShareEnabled != b.ScreenShareEnabled ||
                a.HasAudioTrack != b.HasAudioTrack ||
                !string.Equals(a.AudioTrackId, b.AudioTrackId, StringComparison.Ordinal) ||
                a.HasVideoTrack != b.HasVideoTrack ||
                !string.Equals(a.VideoTrackId, b.VideoTrackId, StringComparison.Ordinal) ||
                a.HasScreenShareTrack != b.HasScreenShareTrack ||
                !string.Equals(a.ScreenShareTrackId, b.ScreenShareTrackId, StringComparison.Ordinal) ||
                a.HasScreenShareAudioTrack != b.HasScreenShareAudioTrack ||
                !string.Equals(a.ScreenShareAudioTrackId, b.ScreenShareAudioTrackId, StringComparison.Ordinal) ||
                a.IsSelf != b.IsSelf)
            {
                return false;
            }
        }

        return true;
    }

    private static string GetExceptionMessage(Exception exception, string fallbackMessage)
    {
        if (!string.IsNullOrWhiteSpace(exception.Message))
            return exception.Message;

        if (exception.InnerException is not null && !string.IsNullOrWhiteSpace(exception.InnerException.Message))
            return exception.InnerException.Message;

        return fallbackMessage;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        DevicePreferences.OnMicrophoneDeviceIdChanged -= OnMicrophoneSelected;
        DevicePreferences.OnCameraDeviceIdChanged -= OnCameraSelected;
        BrowserUtils.Focused -= OnAppResumed;
        _client.VoiceStateService.VoiceParticipantsChanged -= OnVoiceParticipantsChanged;
        _voiceSessionReplaceSubscription?.Dispose();
        _voiceSessionReplaceSubscription = null;
        _voiceModerationSubscription?.Dispose();
        _voiceModerationSubscription = null;

        await LeaveAsync(clearChannel: true);
        _joinLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
