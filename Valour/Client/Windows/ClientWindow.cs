using Valour.Sdk.Models;
using Valour.Client.Components.Windows;
using Valour.Client.Windows.ChatWindows;

namespace Valour.Client.Windows;
public abstract class ClientWindow
{
    /// <summary>
    /// The id of this window
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The holder of this window
    /// </summary>
    public IWindowHolder Holder { get; set; }

    public ClientWindow()
    {
        // Generate random Id for window
        Id = Guid.NewGuid().ToString();
    }

    public abstract Type GetComponentType();

    // TODO: Probably a better way to do this
    public async Task<Planet> GetPlanetAsync()
    {
        if (this is ChatChannelWindow chat)
        {
            return await chat.Channel.GetPlanetAsync();
        }

        return null;
    }

    public virtual async Task OnClosedAsync()
    {
        if (Holder is not null)
            await Holder.CloseWindow(this);
    }

    public async Task CloseAsync()
    {
        await WindowManager.Instance.CloseWindow(this);
    }

    public async Task ReturnHomeAsync()
    {
        var newWindow = new HomeWindow();
        await WindowManager.Instance.ReplaceWindow(this, newWindow);
        await WindowManager.Instance.SetFocusedPlanet(null);
    }
}