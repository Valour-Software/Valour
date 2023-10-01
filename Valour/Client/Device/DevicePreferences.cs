using Blazored.LocalStorage;
using Valour.Client.Messages;

namespace Valour.Client.Device;

public static class DevicePreferences
{
    public static event Func<string, Task> OnMicrophoneDeviceIdChanged;
    
    
    /// <summary>
    /// True if the use wants the (wildly unpopular) auto-emoji feature.
    /// </summary>
    public static bool AutoEmoji { get; set; } = false; // Default it to off
    
    public static string MicrophoneDeviceId { get; set; } = null;
    
    public static async Task SetMicrophoneDeviceId(string deviceId, ILocalStorageService localStorage)
    {
        MicrophoneDeviceId = deviceId;
        await localStorage.SetItemAsync("MicrophoneDeviceId", deviceId);
        
        if (OnMicrophoneDeviceIdChanged is not null)
            await OnMicrophoneDeviceIdChanged.Invoke(deviceId);
    }

    public static async Task LoadPreferences(ILocalStorageService localStorage)
    {
        if (await localStorage.ContainKeyAsync("AutoEmoji"))
        {
            AutoEmoji = await localStorage.GetItemAsync<bool>("AutoEmoji");
        }
        
        if (await localStorage.ContainKeyAsync("MicrophoneDeviceId"))
        {
            MicrophoneDeviceId = await localStorage.GetItemAsync<string>("MicrophoneDeviceId");
        }
        
        // Reload Markdig pipeline
        MarkdownManager.RegenPipeline();
    }
}