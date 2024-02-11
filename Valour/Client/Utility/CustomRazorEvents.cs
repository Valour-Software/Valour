using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Valour.Client.Utility;


[EventHandler("oncontextpress", typeof(ContextPressEventArgs), enableStopPropagation: true, enablePreventDefault: true)]
public static class EventHandlers
{
}

public class ContextPressEventArgs : MouseEventArgs
{
    /// <summary>
    ///
    /// </summary>
    public bool Bubbles { get; set; }

    /// <summary>
    ///
    /// </summary>
    public bool Cancelable { get; set; }

    /// <summary>
    ///
    /// </summary>
    public string SourceElement { get; set; }

    /// <summary>
    ///
    /// </summary>
    public string TargetElement { get; set; }

    /// <summary>
    ///
    /// </summary>
    public double TimeStamp { get; set; }
}