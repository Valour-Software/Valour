using Microsoft.AspNetCore.Components;
using Valour.Client.Utility;

namespace Valour.Client.Components.Tooltips;

/// <summary>
/// Something a tooltip can be shown for: a <see cref="TooltipTrigger"/>
/// component, or an element found by a shared listener, such as a reaction.
/// </summary>
public interface ITooltipSource
{
    /// <summary>
    /// Identifies the source, so showing it again reuses its open tooltip.
    /// </summary>
    string TriggerId { get; }

    RenderFragment TooltipContent { get; }

    /// <summary>
    /// The side of the source to place the tooltip on: top, bottom, left or right.
    /// </summary>
    string Position { get; }

    string TooltipClass { get; }

    /// <summary>
    /// The source's bounds in viewport coordinates. The tooltip hides when the
    /// mouse leaves them.
    /// </summary>
    Task<ElementBounds> GetBoundsAsync();
}
