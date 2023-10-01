using Blazored.LocalStorage;
using Valour.Client.Messages;

namespace Valour.Client.Device;

public static class DevicePreferences
{
    /// <summary>
    /// True if the use wants the (wildly unpopular) auto-emoji feature.
    /// </summary>
    public static bool AutoEmoji { get; set; } = false; // Default it to off
    
    public static string MicrophoneDeviceId { get; set; } = null;

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