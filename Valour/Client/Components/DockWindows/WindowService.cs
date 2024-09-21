using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Sdk.Models;

namespace Valour.Client.Components.DockWindows;

public static class WindowService
{
    public static event Func<WindowTab, Task> OnFocusedTabChanged;
    public static event Func<Planet, Task> OnFocusedPlanetChanged;
    
    public static List<WindowDockComponent> Docks { get; private set; } = new();
    public static WindowDockComponent MainDock { get; private set; }
    
    public static List<WindowTab> GlobalTabs { get; private set; } = new();
    public static List<ChatChannelWindowComponent> GlobalChatTabs { get; private set; } = new();
    
    public static WindowTab FocusedTab { get; private set; }
    
    public static Planet FocusedPlanet { get; private set; }
    
    public static async Task SetFocusedTab(WindowTab tab)
    {
        if (FocusedTab == tab)
            return;
        
        FocusedTab = tab;
        
        if (OnFocusedTabChanged is not null)
            await OnFocusedTabChanged.Invoke(tab);
        
        if (tab.Content.PlanetId is not null)
        {
            FocusedPlanet = await Planet.FindAsync(tab.Content.PlanetId.Value);
            
            if (OnFocusedPlanetChanged is not null)
                await OnFocusedPlanetChanged.Invoke(FocusedPlanet);
        }
    }
    
    public static void AddDock(WindowDockComponent dock)
    {
        // Set the main dock if it is not set
        if (MainDock is null)
            MainDock = dock;

        if (Docks.Contains(dock))
            return;

        Docks.Add(dock);
    }

    public static void NotifyDockLayoutUpdated()
    {
        // Clear old window lists
        GlobalTabs.Clear();
        GlobalChatTabs.Clear();
        
        // Add all windows to the global lists
        foreach (var dock in Docks)
        {
            GlobalTabs.AddRange(dock.Tabs);
            
            var chatTabs = dock.Tabs.Select(x => x.Content.ComponentBase)
                .OfType<ChatChannelWindowComponent>().ToList();
            
            GlobalChatTabs.AddRange(chatTabs);
        }
    }

    public static void ResetAll()
    {
        foreach (var dock in Docks)
        {
            dock.Reset(false);
        }
        
        // Handle this ourselves to avoid multiple calls to NotifyDockLayoutUpdated
        NotifyDockLayoutUpdated();
    }

    public static async Task OpenWindowAtFocused(WindowContent content)
    {
        var tab = new WindowTab(content);
        await OpenWindowAtFocused(tab);
    }
    
    public static async Task OpenWindowAtFocused(WindowTab tab)
    {
        if (FocusedTab is not null)
        {
            await FocusedTab.Layout.AddTab(tab);
        }
        else
        {
            await TryAddFloatingWindow(tab);
        } 
    }
    
    /// <summary>
    /// Adds the given window as a floating window if supported on the device,
    /// otherwise adds it in a supported manner
    /// </summary>
    public static Task TryAddFloatingWindow(WindowTab tab, FloatingWindowProps props = null)
    {
        return MainDock.AddFloatingTab(tab, props);
    }
    
    /// <summary>
    /// Adds the given window content as a floating window if supported on the device,
    /// otherwise adds it in a supported manner
    /// </summary>
    public static async Task TryAddFloatingWindow(WindowContent content, FloatingWindowProps props = null)
    {
        await MainDock.AddFloatingTab(content, props);
    }
}