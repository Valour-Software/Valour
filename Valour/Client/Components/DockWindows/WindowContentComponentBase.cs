using Microsoft.AspNetCore.Components;
using Valour.Client.Components.Utility;

namespace Valour.Client.Components.DockWindows;

public class WindowContentComponentBase : ControlledRenderComponentBase
{
    [Parameter]
    public WindowContent WindowCtx { get; set; }

    /// <summary>
    /// Links the window content to this component. This runs here rather than
    /// in OnInitialized because subclasses override OnInitialized without
    /// calling the base method.
    /// </summary>
    public override Task SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        WindowCtx?.SetComponent(this);
        return base.SetParametersAsync(ParameterView.Empty);
    }

    /// <summary>
    /// Called by the hosting window when the content is shown or hidden, for
    /// example when another tab in the same stack is selected. Content that is
    /// not hosted in a window is always visible.
    /// </summary>
    public virtual void OnWindowVisibilityChanged(bool visible)
    {
    }
}

public class WindowContentComponent<T> : WindowContentComponentBase
{
    [Parameter]
    public T Data { get; set; }
}