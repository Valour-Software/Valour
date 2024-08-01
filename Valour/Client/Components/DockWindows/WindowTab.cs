using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Sdk.Client;
using Valour.Sdk.Extensions;
using Valour.Sdk.Models;

namespace Valour.Client.Components.DockWindows;

public static class WindowService
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
    
    public static WindowTab GlobalActiveWindow { get; set; }
    public static event Func<WindowTab, Task> OnActiveWindowChange;
    
    public static async Task ReplaceGlobalActiveWindowAsync(WindowTab newTab)
    {
        if (newTab is null)
        {
            return;
        }

        if (GlobalActiveWindow is not null)
        {
            await GlobalActiveWindow.WindowBase.ReplaceAsync(newTab);
        }
        else
        {
            // If there is no window, create one
            await DockContainer.MainDock.AddWindowAsync(newTab);
            await SetGlobalActiveWindowAsync(newTab);
        }

        if (OnActiveWindowChange is not null)
            await OnActiveWindowChange.Invoke(GlobalActiveWindow);

        await GlobalActiveWindow.NotifyFocused();
    }

    public static async Task SetGlobalActiveWindowAsync(WindowTab window)
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
            if (window.Content?.PlanetId is not null)
            {
                if (OnActivePlanetChange is not null)
                {
                    var planet = await Planet.FindAsync(window.Content.PlanetId.Value);
                    await OnActivePlanetChange.Invoke(planet);
                }
            }
            
            await window.NotifyFocused();
        }
    }
    
    public static List<WindowTab> GlobalWindows { get; set; } = new();
    
    public static void AddGlobalWindow(WindowTab window)
    {
        if (GlobalWindows.Contains(window))
            return;
        
        GlobalWindows.Add(window);
    }
    
    public static void RemoveGlobalWindow(WindowTab window)
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

    public static async Task OpenWindowAtActive(WindowTab tab)
    {
        if (GlobalActiveWindow is not null)
        {
            await GlobalActiveWindow.WindowBase.AddSiblingWindow(tab);
        }
        else
        {
            await DockContainer.MainDock.AddWindowAsync(tab);
        }
    }
    
    /// <summary>
    /// Adds the given window as a floating window if supported on the device,
    /// otherwise adds it in a supported manner
    /// </summary>
    public static async Task TryAddFloatingWindow(WindowTab tab)
    {
        if (DockFloaters.Instance is not null)
        {
            await DockFloaters.Instance.AddFloater(tab, 100, 100);
        }
        else
        {
            await DockContainer.MainDock.AddWindowAsync(tab);
        }
    }
}

public class WindowContent
{
    public WindowTab Tab { get; set; }
    
    public string Title { get; set; }
    public string Icon { get; set; }
    public Type Type { get; set; }
    public object Data { get; set; }
    public long? PlanetId { get; set; }
    public bool AutoScroll { get; set; }
}

public class WindowTab
{
    public event Func<Task> OnFocused;
    public event Func<Task> OnClosed;
    public event Func<Task> OnOpened;
    
    public List<WindowContent> History { get; set; } = new();
    public WindowContent Content { get; private set; }

    public WindowBase WindowBase { get; set; }
    
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public double StartFloatX { get; set; }
    public double StartFloatY { get; set; }
    public int StartFloatWidth { get; set; } = 400;
    public int StartFloatHeight { get; set; } = 400;
    public bool Rendered { get; set; }
    
    public void SetContent(WindowContent content)
    {
        Content = content;
        Rendered = false;
    }

    public async Task NotifyAdded()
    {
        if (Content?.PlanetId is not null)
        {
            var planet = await Planet.FindAsync(Content.PlanetId.Value);
            await ValourClient.OpenPlanetConnection(planet, Id);
        }
    }
    
    public async Task NotifyFocused()
    {
        if (OnFocused is not null)
            await OnFocused.Invoke();
    }
    
    public async Task NotifyClose()
    {
        if (OnClosed is not null)
            await OnClosed.Invoke();

        if (Content?.PlanetId is not null)
        {
            var planet = await Planet.FindAsync(Content.PlanetId.Value);
            await ValourClient.ClosePlanetConnection(planet, Id);
        }
    }
    
    public async Task NotifyOpen()
    {
        if (OnOpened is not null)
            await OnOpened.Invoke();
    }

    public async Task NotifyRendered()
    {
        if (!Rendered)
        {
            await NotifyOpen();
        }
        
        Rendered = true;
    }

}