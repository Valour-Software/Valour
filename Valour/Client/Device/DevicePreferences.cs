using Valour.Client.Messages;
using Valour.Client.Storage;
using Valour.Client;
using Microsoft.JSInterop;

namespace Valour.Client.Device;

public static class DevicePreferences
{
    public const string ErrorReportingEnabledStorageKey = "ErrorReportingEnabled";
    public const string ForceGpuAccelerationStorageKey = "ForceGpuAcceleration";

    public static event Func<string?, Task>? OnMicrophoneDeviceIdChanged;
    public static event Func<string?, Task>? OnCameraDeviceIdChanged;


    /// <summary>
    /// True if the use wants the (wildly unpopular) auto-emoji feature.
    /// </summary>
    public static bool AutoEmoji { get; set; } = false; // Default it to off

    /// <summary>
    /// True if messages containing only a few emojis (and no other text) should
    /// be rendered larger, the same size as a heading.
    /// </summary>
    public static bool BigEmojiMessages { get; set; } = true;

    public static string? MicrophoneDeviceId { get; set; }
    public static string? CameraDeviceId { get; set; }
    public static bool ErrorReportingEnabled { get; private set; }
    public static bool ForceGpuAcceleration { get; private set; } = true;

    public static async Task SetMicrophoneDeviceId(string? deviceId, IAppStorage localStorage)
    {
        MicrophoneDeviceId = deviceId;
        await localStorage.SetAsync("MicrophoneDeviceId", deviceId);

        if (OnMicrophoneDeviceIdChanged is not null)
            await OnMicrophoneDeviceIdChanged.Invoke(deviceId);
    }

    public static async Task SetCameraDeviceId(string? deviceId, IAppStorage localStorage)
    {
        CameraDeviceId = deviceId;
        await localStorage.SetAsync("CameraDeviceId", deviceId);

        if (OnCameraDeviceIdChanged is not null)
            await OnCameraDeviceIdChanged.Invoke(deviceId);
    }

    public static async Task SetErrorReportingEnabled(bool isEnabled, IAppStorage localStorage)
    {
        ErrorReportingEnabled = isEnabled;
        SentryGate.IsEnabled = isEnabled;
        await localStorage.SetAsync(ErrorReportingEnabledStorageKey, isEnabled);
    }

    public static async Task SetBigEmojiMessages(bool isEnabled, IAppStorage localStorage)
    {
        BigEmojiMessages = isEnabled;
        await localStorage.SetAsync("BigEmojiMessages", isEnabled);
    }
  
    public static async Task SetForceGpuAccelerationEnabled(bool isEnabled, IAppStorage localStorage)
    {
        ForceGpuAcceleration = isEnabled;
        await localStorage.SetAsync(ForceGpuAccelerationStorageKey, isEnabled);
    }

    public static async Task ApplyForceGpuAccelerationAsync(IJSRuntime jsRuntime)
    {
        await jsRuntime.InvokeVoidAsync("valourGpuAcceleration.setEnabled", ForceGpuAcceleration);
    }

    public static async Task LoadPreferences(IAppStorage localStorage)
    {
        if (await localStorage.ContainsKeyAsync("AutoEmoji"))
        {
            AutoEmoji = await localStorage.GetAsync<bool>("AutoEmoji");
        }

        if (await localStorage.ContainsKeyAsync("BigEmojiMessages"))
        {
            BigEmojiMessages = await localStorage.GetAsync<bool>("BigEmojiMessages");
        }

        if (await localStorage.ContainsKeyAsync("MicrophoneDeviceId"))
        {
            MicrophoneDeviceId = await localStorage.GetAsync<string>("MicrophoneDeviceId");
        }

        if (await localStorage.ContainsKeyAsync("CameraDeviceId"))
        {
            CameraDeviceId = await localStorage.GetAsync<string>("CameraDeviceId");
        }

        if (await localStorage.ContainsKeyAsync(ErrorReportingEnabledStorageKey))
        {
            ErrorReportingEnabled = await localStorage.GetAsync<bool>(ErrorReportingEnabledStorageKey);
        }
        else
        {
            ErrorReportingEnabled = false;
        }

        ForceGpuAcceleration = !await localStorage.ContainsKeyAsync(ForceGpuAccelerationStorageKey)
            || await localStorage.GetAsync<bool>(ForceGpuAccelerationStorageKey);

        SentryGate.IsEnabled = ErrorReportingEnabled;

        // Reload Markdig pipeline
        MarkdownManager.RegenPipeline();
    }
}
