using Valour.Client.Components.Windows;

namespace Valour.Client.Windows;
public abstract class ClientWindow
{
    /// <summary>
    /// The id of this window
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// True if a render is needed
    /// </summary>
    public bool NeedsRender { get; set; }

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

    public virtual async Task OnClosedAsync()
    {
        if (Holder is not null)
            await Holder.CloseWindow(this);
    }
}