using Valour.Client.Blazor.Shared.Windows.HomeWindows;

namespace Valour.Client.Blazor.Windows;

public class HomeWindow : ClientWindow
{
    public override Type GetComponentType() =>
        typeof(HomeWindowComponent);
    public HomeWindow(int index) : base(index)
    {

    }
}