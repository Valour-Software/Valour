using Valour.Api.Client;
using Valour.Api.Items.Planets;
using Valour.Client.Components.Windows;
using Valour.Api.Items;
using Valour.Shared;
using Valour.Api.Nodes;
using Valour.Api.Items.Channels.Planets;

namespace Valour.Client.Windows;

public class WindowManager
{
    /// <summary>
    /// A statically accessible instance of the WindowManager.
    /// This should usually be accessed via DI
    /// </summary>
    public static WindowManager Instance { get; set; }

    // Private window store
    private List<ClientWindow> Windows { get; set; } = new();

    /// <summary>
    /// Returns all the current windows
    /// </summary>
    public List<ClientWindow> GetWindows() => Windows;

    /// <summary>
    /// The total number of current windows
    /// </summary>
    public int WindowCount => Windows.Count;

    /// <summary>
    /// The planet currently focused by the user (last clicked)
    /// </summary>
    public Planet FocusedPlanet { get; private set; }

    /// <summary>
    /// The window currently selected by the user (last clicked)
    /// </summary>
    private ClientWindow SelectedWindow;

    /// <summary>
    /// Event for when a new window is selected
    /// </summary>
    public event Func<Task> OnWindowSelect;

    /// <summary>
    /// Event for when a new planet is focused on
    /// </summary>
    public event Func<Planet, Task> OnPlanetFocused;

    /// <summary>
    /// The component for the main windows
    /// </summary>
    public MainWindowsComponent MainWindowsComponent { get; set; }

    public WindowManager()
    {
        Instance = this;
        ValourClient.OnNodeReconnect += OnNodeReconnect;
        ItemObserver<Planet>.OnAnyDeleted += OnPlanetDelete;
    }

    public async Task Log(string msg)
    {
        await Logger.Log(msg, "purple");
    }

    /// <summary>
    /// Handles planet delete events (spooky!)
    /// </summary
    public async Task OnPlanetDelete(Planet planet)
    {
        var chatWindows = Windows.OfType<ChatChannelWindow>();

        // Close all channels belonging to the planet
        foreach (var chat in chatWindows.Where(x => x.Channel.PlanetId == planet.Id))
            await CloseWindow(chat);
    }

    /// <summary>
    /// Adds a window to the client
    /// </summary>
    public async Task AddWindowAsync(ClientWindow window, IWindowHolder target)
    {
        window.Holder = target;

        // Add window to list
        Windows.Add(window);

        // Add window to the specified target
        await target.OpenWindow(window);

        await Log($"[WindowManager]: Added window {window.Id}");
    }


    public async Task SetFocusedPlanet(Planet planet)
    {

        if (planet is null)
        {
            if (FocusedPlanet is null)
                return;
        }
        else
        {
            if (FocusedPlanet is not null)
                if (planet.Id == FocusedPlanet.Id)
                    return;
        }

        FocusedPlanet = planet;

        // Ensure focused planet is open
        await ValourClient.OpenPlanet(planet);

        if (planet is null)
            await Log("[WindowManager]: Set current planet to null");
        else
            await Log($"[WindowManager]: Set focused planet to {planet.Name} ({planet.Id})");

        await OnPlanetFocused?.Invoke(planet);
    }

    public async Task OnNodeReconnect(Node node)
    {
        await ForceChatRefresh();
    }

    /// <summary>
    /// Swaps the channel a chat channel window is showing
    /// </summary>
    public async Task SwapWindowChannel(ChatChannelWindow window, PlanetChatChannel newChannel)
    {
        // Already that channel
        if (window.Channel.Id == newChannel.Id)
            return;

        await Log("[WindowManager]: Swapping chat channel " + window.Channel.Name + " for " + newChannel.Name);

        var chatWindows = Windows.OfType<ChatChannelWindow>();

        var oldChannel = window.Channel;

        // Set window's new channel
        window.Channel = newChannel;

        if (!(chatWindows.Any(x => x.Id != window.Id && x.Channel.Id == oldChannel.Id)))
        {
            // Close the channel connection if this is the only open window for it
            await ValourClient.CloseChannelConnection(oldChannel);

            // We may have to close the planet if the new channel is not
            // from the same planet
            if (newChannel.PlanetId != oldChannel.PlanetId)
                await ClosePlanetConnectionIfNeeded(await oldChannel.GetPlanetAsync());
        }

        // Open connection to channel
        await ValourClient.OpenChannel(newChannel);
    }

    /// <summary>
    /// Closes planet connection if no chat channels are opened for it
    /// </summary>
    public async Task ClosePlanetConnectionIfNeeded(Planet planet)
    {
        if (planet == null)
            return;

        var chatWindows = Windows.OfType<ChatChannelWindow>();

        // Check if any open chat windows are for the planet
        if (!chatWindows.Any(x => x.Channel.PlanetId == planet.Id))
        {
            // Close the planet connection
            await ValourClient.ClosePlanetConnection(planet);

            // Remove focus if it's the focused planet
            if (FocusedPlanet == planet)
                await SetFocusedPlanet(null);
        }
    }

    /// <summary>
    /// Sets the window selected by the user (last interacted with)
    /// </summary>
    public async Task SetSelectedWindow(ClientWindow window)
    {
        // Check to ensure window is new and valid
        if (window == null || (SelectedWindow == window)) return;

        // Set selected window
        SelectedWindow = window;

        await Log($"[WindowManager]: Set active window to {window.Id}");

        // If Chat Channel, set focused planet to the channel's planet
        if (window is ChatChannelWindow)
        {
            var chatW = window as ChatChannelWindow;
            await SetFocusedPlanet(await chatW.Channel.GetPlanetAsync());
        }


        // Run event for window selection
        if (OnWindowSelect != null)
        {
            await Log($"[WindowManager]: Invoking window change event");
            await OnWindowSelect?.Invoke();
        }
    }

    /// <summary>
    /// Returns the currently selected window
    /// </summary>
    public ClientWindow GetSelectedWindow()
    {
        return SelectedWindow;
    }

    /// <summary>
    /// Returns the total number of windows
    /// </summary>
    public int GetWindowCount()
    {
        return Windows.Count;
    }

    /// <summary>
    /// Returns the window with the given id
    /// </summary>
    public ClientWindow GetWindow(string id) =>
        Windows.FirstOrDefault(x => x.Id == id);
        
    /// <summary>
    /// Clears all of the windows
    /// </summary>
    public async Task ClearWindows()
    {
        var toClose = Windows.ToList();
        foreach (ClientWindow window in toClose)
        {
            await CloseWindow(window);
        }
    }

    /// <summary>
    /// Replaces a window with another window
    /// </summary>
    public async Task ReplaceWindow(ClientWindow old, ClientWindow newWindow)
    {
        if (old.Id == newWindow.Id)
            return;

        Windows.Add(newWindow);

        newWindow.Holder = old.Holder;

        await old.Holder.ReplaceWindow(old, newWindow);

        // Don't refresh! We're doing that ourselves
        await CloseWindow(old, false);

        await ForceChatRefresh();
    }

    /// <summary>
    /// Closes the given window
    /// </summary>
    public async Task CloseWindow(ClientWindow window, bool refresh = true)
    {
        await window.OnClosedAsync();

        Windows.Remove(window);

        if (window is ChatChannelWindow)
        {
            var chatWindow = window as ChatChannelWindow;
            var chatWindows = Windows.OfType<ChatChannelWindow>();

            if (!chatWindows.Any(x => x.Id != window.Id))
            {

                // Close the channel connection if this is the only open window for it
                await ValourClient.CloseChannelConnection(chatWindow.Channel);
            }

            await ClosePlanetConnectionIfNeeded(await chatWindow.Channel.GetPlanetAsync());
        }
    }

    public async Task ForceChatRefresh()
    {
        foreach (var chat in Windows.OfType<ChatChannelWindow>())
        {
            if (chat != null && chat.Component != null && chat.Component.MessageHolder != null)
            {
                // Force full window refresh
                await chat.Component.SetupNewChannelAsync();
            }
        }
    }
}
