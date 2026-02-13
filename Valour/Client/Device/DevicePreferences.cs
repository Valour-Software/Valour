using Valour.Client.Messages;
using Valour.Client.Storage;

namespace Valour.Client.Device;

public static class DevicePreferences
{
    public static event Func<string, Task> OnMicrophoneDeviceIdChanged;


    /// <summary>
    /// True if the use wants the (wildly unpopular) auto-emoji feature.
    /// </summary>
    public static bool AutoEmoji { get; set; } = false; // Default it to off

    public static string MicrophoneDeviceId { get; set; } = null;

    public static async Task SetMicrophoneDeviceId(string deviceId, IAppStorage localStorage)
    {
        MicrophoneDeviceId = deviceId;
        await localStorage.SetAsync("MicrophoneDeviceId", deviceId);

        if (OnMicrophoneDeviceIdChanged is not null)
            await OnMicrophoneDeviceIdChanged.Invoke(deviceId);
    }

    public static async Task LoadPreferences(IAppStorage localStorage)
    {
        if (await localStorage.ContainsKeyAsync("AutoEmoji"))
        {
            AutoEmoji = await localStorage.GetAsync<bool>("AutoEmoji");
        }

        if (await localStorage.ContainsKeyAsync("MicrophoneDeviceId"))
        {
            MicrophoneDeviceId = await localStorage.GetAsync<string>("MicrophoneDeviceId");
        }

        // Reload Markdig pipeline
        MarkdownManager.RegenPipeline();
    }
}