using Valour.Client.Components.Windows.CallWindows;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Client.Device;
using Valour.Sdk.Models;
using Valour.Shared.Utilities;

namespace Valour.Client.Components.DockWindows;

public static class WindowService
{
    private static readonly SemaphoreSlim MobileNavigationLock = new(1, 1);

    public static HybridEvent<WindowTab> FocusedTabChanged;
    public static HybridEvent<Planet> FocusedPlanetChanged;

    public static event Action<WindowTab> OnTabDragging;
    public static WindowTab DraggingTab { get; set; }

    public static List<WindowDockComponent> Docks { get; private set; } = new();
    public static WindowDockComponent MainDock { get; private set; }

    public static List<WindowTab> GlobalTabs { get; private set; } = new();
    public static List<ChatWindowComponent> GlobalChatTabs { get; private set; } = new();

    public static WindowTab FocusedTab { get; private set; }

    public static Planet FocusedPlanet { get; private set; }

    
    public static async Task SetFocusedTab(WindowTab tab)
    {
        if (FocusedTab == tab)
            return;

        FocusedTab = tab;
        await NotifyFocusedContentChanged(tab);
    }

    /// <summary>
    /// Refreshes focus-derived state when a mobile tab keeps its identity but
    /// replaces its content.
    /// </summary>
    internal static async Task NotifyFocusedContentChanged(WindowTab tab)
    {
        if (FocusedTab != tab)
            return;

        FocusedTabChanged?.Invoke(tab);

        FocusedPlanet = tab.Content.PlanetId is not null
            ? await MainDock.Client.PlanetService.FetchPlanetAsync(tab.Content.PlanetId.Value)
            : null;

        FocusedPlanetChanged?.Invoke(FocusedPlanet);
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
    
    public static void NotifyTabDragging(WindowTab tab)
    {
        DraggingTab = tab;
        
        if (OnTabDragging is not null)
            OnTabDragging.Invoke(tab);
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
                .OfType<ChatWindowComponent>().ToList();
            
            GlobalChatTabs.AddRange(chatTabs);
        }
    }

    public static void ResetAll()
    {
        foreach (var dock in Docks)
        {
            _ = dock.Reset(false);
        }
        
        // Handle this ourselves to avoid multiple calls to NotifyDockLayoutUpdated
        NotifyDockLayoutUpdated();
    }

    public static async Task OpenWindowAtFocused(WindowContent content, WindowTab tabToReplace = null)
    {
        if (DeviceInfo.IsMobile)
        {
            await MobileNavigationLock.WaitAsync();
            try
            {
                await OpenMobileWindowAsync(content);
            }
            finally
            {
                MobileNavigationLock.Release();
            }

            return;
        }

        if (await TryFocusExistingWindowForContent(content))
            return;

        // Replace the given tab's content instead of adding a new tab
        if (tabToReplace?.Component is not null)
        {
            await tabToReplace.Component.ReplaceAsync(content);
            return;
        }

        var tab = new WindowTab(content);
        await OpenWindowAtFocused(tab);
    }
    
    public static async Task OpenWindowAtFocused(WindowTab tab)
    {
        if (DeviceInfo.IsMobile)
        {
            await MobileNavigationLock.WaitAsync();
            try
            {
                await OpenMobileWindowAsync(tab.Content, tab);
            }
            finally
            {
                MobileNavigationLock.Release();
            }

            return;
        }

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
    /// Mobile has one logical window. Reuse that tab so component teardown,
    /// connection ownership, and layout rendering happen as one serialized
    /// operation instead of racing an add followed by a remove.
    /// </summary>
    private static async Task OpenMobileWindowAsync(WindowContent content, WindowTab newTab = null)
    {
        if (content is null || MainDock?.Layout is null)
            return;

        var mainWindow = MainDock.Layout.FocusedTab
                         ?? MainDock.Layout.Tabs.FirstOrDefault();

        if (mainWindow is null)
        {
            await MainDock.Layout.AddTab(newTab ?? new WindowTab(content));
            return;
        }

        // Repair layouts left in an invalid multi-tab state by older mobile
        // navigation. Hidden tabs still own components and connection locks.
        var extraTabs = MainDock.Layout.Tabs
            .Where(x => x != mainWindow)
            .ToList();

        foreach (var extraTab in extraTabs)
            await MainDock.Layout.RemoveTab(extraTab, false);

        if (!RepresentsSameContent(mainWindow.Content, content))
            await mainWindow.SetContent(content);

        if (extraTabs.Count > 0)
            await MainDock.NotifyLayoutChanged();
    }
    
    /// <summary>
    /// Adds the given window as a floating window if supported on the device,
    /// otherwise adds it in a supported manner
    /// </summary>
    public static Task TryAddFloatingWindow(WindowTab tab, FloatingWindowProps props = null)
    {
        if (tab?.Content is not null)
        {
            // Keep floating-window entrypoints aligned with OpenWindowAtFocused duplicate checks.
            var duplicateFocusTask = TryFocusExistingWindowForContent(tab.Content);
            if (duplicateFocusTask.IsCompletedSuccessfully && duplicateFocusTask.Result)
                return Task.CompletedTask;

            return TryAddFloatingWindowInternalAsync(tab, props, duplicateFocusTask);
        }

        return MainDock.AddFloatingTab(tab, props);
    }

    private static async Task TryAddFloatingWindowInternalAsync(
        WindowTab tab,
        FloatingWindowProps props,
        Task<bool> duplicateFocusTask)
    {
        if (await duplicateFocusTask)
            return;

        await MainDock.AddFloatingTab(tab, props);
    }

    private static async Task<bool> TryFocusExistingWindowForContent(WindowContent content)
    {
        if (content is null)
            return false;

        var existingTab = GlobalTabs.FirstOrDefault(t =>
            RepresentsSameContent(t.Content, content));

        if (existingTab?.Layout is null)
            return false;

        await existingTab.Layout.SetFocusedTab(existingTab);
        await existingTab.Layout.DockComponent.NotifyLayoutChanged();
        return true;
    }

    private static bool RepresentsSameContent(WindowContent existing, WindowContent requested)
    {
        if (existing is ChatWindowComponent.Content existingChat &&
            requested is ChatWindowComponent.Content requestedChat)
        {
            return existingChat.Data is not null &&
                   requestedChat.Data is not null &&
                   existingChat.Data.Id == requestedChat.Data.Id;
        }

        if (existing is CallWindowComponent.Content existingCall &&
            requested is CallWindowComponent.Content requestedCall)
        {
            return existingCall.Data is not null &&
                   requestedCall.Data is not null &&
                   existingCall.Data.Id == requestedCall.Data.Id;
        }

        return ReferenceEquals(existing, requested);
    }
    
    /// <summary>
    /// Adds the given window content as a floating window if supported on the device,
    /// otherwise adds it in a supported manner
    /// </summary>
    public static async Task TryAddFloatingWindow(WindowContent content, FloatingWindowProps props = null)
    {
        if (DeviceInfo.IsMobile)
        {
            // Mobile devices do not support floating windows
            // Add the window to the main dock
            await OpenWindowAtFocused(content);
            return;
        }

        if (await TryFocusExistingWindowForContent(content))
            return;

        await MainDock.AddFloatingTab(content, props);
    }

}
