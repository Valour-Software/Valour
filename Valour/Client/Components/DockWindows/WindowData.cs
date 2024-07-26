using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Sdk.Models;

namespace Valour.Client.Components.DockWindows;

public static class GlobalWindowData
{
    public static Planet GlobalActivePlanet { get; set; }
    public static event Func<Planet, Task> OnActivePlanetChange;
    
    public static async Task SetGlobalActivePlanetAsync(Planet planet)
    {
        if (planet.Id == GlobalActivePlanet?.Id)
            return;
        
        GlobalActivePlanet = planet;
        
        if (OnActivePlanetChange is not null)
            await OnActivePlanetChange.Invoke(planet);
    }
    
    public static WindowData GlobalActiveWindow { get; set; }
    public static event Func<WindowData, Task> OnActiveWindowChange;

    public static async Task SetGlobalActiveWindowAsync(WindowData window)
    {
        if (window is null)
        {
            if (GlobalActiveWindow is null)
            {
                return;
            }
        }
        else if (window.Id == GlobalActiveWindow?.Id) {
            return;
        }

        GlobalActiveWindow = window;
        if (OnActiveWindowChange is not null)
            await OnActiveWindowChange.Invoke(window);

        if (window is not null)
        {
            await window.NotifyFocused();
        }
    }
    
    public static List<WindowData> GlobalWindows { get; set; } = new();
    
    public static void AddGlobalWindow(WindowData window)
    {
        if (GlobalWindows.Contains(window))
            return;
        
        GlobalWindows.Add(window);
    }
    
    public static void RemoveGlobalWindow(WindowData window)
    {
        if (!GlobalWindows.Contains(window))
            return;
        
        GlobalWindows.Remove(window);
    }
    
    public static List<ChatChannelWindowComponent> GlobalChatWindows { get; set; } = new();
    
    public static void AddGlobalChatWindow(ChatChannelWindowComponent window)
    {
        if (GlobalChatWindows.Contains(window))
            return;
        
        GlobalChatWindows.Add(window);
    }
    
    public static void RemoveGlobalChatWindow(ChatChannelWindowComponent window)
    {
        if (!GlobalChatWindows.Contains(window))
            return;
        
        GlobalChatWindows.Remove(window);
    }

    public static async Task OpenWindowAtActive(WindowData data)
    {
        if (GlobalActiveWindow is not null)
        {
            await GlobalActiveWindow.WindowBase.AddSiblingWindow(data);
        }
        else
        {
            await DockContainer.MainDock.AddWindowAsync(data);
        }
    }
    
    /// <summary>
    /// Adds the given window as a floating window if supported on the device,
    /// otherwise adds it in a supported manner
    /// </summary>
    public static async Task TryAddFloatingWindow(WindowData data)
    {
        
        
        if (DockFloaters.Instance is not null)
        {
            await DockFloaters.Instance.AddFloater(data);
        }
        else
        {
            await DockContainer.MainDock.AddWindowAsync(data);
        }
    }
}

public class WindowData
{
    public event Func<Task> OnFocused;
    public event Func<Task> OnClosed;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; }
    public string Icon { get; set; }
    public Type Type { get; set; }
    public object Data { get; set; }
    public WindowBase WindowBase { get; set; }
    public double? StartFloatX { get; set; }
    public double? StartFloatY { get; set; }
    public int StartFloatWidth { get; set; } = 400;
    public int StartFloatHeight { get; set; } = 400;
    public bool AutoScroll { get; set; } = false;
    public bool StartDragging { get; set; } = false;

    public async Task NotifyFocused()
    {
        if (OnFocused is not null)
            await OnFocused.Invoke();
    }
    
    public async Task NotifyClose()
    {
        if (OnClosed is not null)
            await OnClosed.Invoke();
    }

}