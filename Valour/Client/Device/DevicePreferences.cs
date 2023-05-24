using Blazored.LocalStorage;
using Valour.Client.Messages;

namespace Valour.Client.Device;

public static class DevicePreferences
{
    /// <summary>
    /// True if the use wants the (wildly unpopular) auto-emoji feature.
    /// </summary>
    public static bool AutoEmoji { get; set; } = false; // Default it to off

    public static async Task LoadPreferences(ILocalStorageService localStorage)
    {
        if (await localStorage.ContainKeyAsync("AutoEmoji"))
        {
            AutoEmoji = await localStorage.GetItemAsync<bool>("AutoEmoji");
        }
        
        // Reload Markdig pipeline
        MarkdownManager.RegenPipeline();
    }
}