using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.Utility;

public class CustomDropdownContentData
{
    public List<object> Items { get; set; } = new();
    public RenderFragment<object> ItemTemplate { get; set; }
    public Func<object, string, bool> SearchFunc { get; set; }
    public Func<object, Task> OnSelect { get; set; }
}
