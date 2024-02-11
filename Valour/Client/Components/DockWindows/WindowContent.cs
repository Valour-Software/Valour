using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.DockWindows;

public class WindowContent<T> : ComponentBase
{
    [Parameter]
    public T Data { get; set; }
    
    [Parameter]
    public WindowData Window { get; set; }
}