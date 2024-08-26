using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.DockWindows;

public class WindowContentComponent : ComponentBase 
{
    [Parameter]
    public WindowContent WindowCtx { get; set; }

    protected override void OnInitialized()
    {
        // Set the window content component automatically
        WindowCtx.SetComponent(this);
    }
}

public class WindowContentComponent<T> : WindowContentComponent
{
    [Parameter]
    public T Data { get; set; }
}