using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.Tooltips;

public class TooltipData
{
    public Tooltip Component { get; set; }
    public TooltipTrigger Trigger { get; set; }
}