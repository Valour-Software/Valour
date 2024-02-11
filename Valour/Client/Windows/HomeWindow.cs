using Valour.Client.Components.Windows.HomeWindows;

namespace Valour.Client.Windows;

public class HomeWindow : ClientWindow
{
    public override Type GetComponentType() =>
        typeof(HomeWindowComponent);
    public HomeWindow()
    {

    }
}