using Valour.Client.Shared.Windows;

namespace Valour.Client.Windows;
public abstract class ClientWindow
{
    /// <summary>
    /// The index of this window
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// True if a render is needed
    /// </summary>
    public bool NeedsRender { get; set; }

    public ClientWindow(int index)
    {
        this.Index = index;
    }

    public abstract Type GetComponentType();

    public virtual void OnClosed()
    {

    }
}