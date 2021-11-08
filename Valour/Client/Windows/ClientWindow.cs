namespace Valour.Client.Windows;

using Valour.Client.Shared.Windows;

public class ClientWindow
{
    /// <summary>
    /// The index of this window
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// True if a render is needed
    /// </summary>
    public bool NeedsRender { get; set; }

    /// <summary>
    /// The main window parent component
    /// </summary>
    public MainWindowComponent MainComponent { get; set; }

    public ClientWindow(int index)
    {
        this.Index = index;
    }

    public virtual void OnClosed()
    {

    }
}