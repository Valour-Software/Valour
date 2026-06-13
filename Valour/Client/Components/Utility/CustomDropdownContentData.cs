using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.Utility;

public class CustomDropdownContentData
{
    public List<object> Items { get; set; } = new();
    public RenderFragment<object> ItemTemplate { get; set; }
    public Func<object, string, bool> SearchFunc { get; set; }
    public Func<object, Task> OnSelect { get; set; }
    public object SelectedItem { get; set; }

    /// <summary>
    /// When set, a "clear selection" option is shown at the top of the list which selects null
    /// </summary>
    public RenderFragment NullOptionTemplate { get; set; }

    /// <summary>
    /// Hides the search box for short, fixed lists
    /// </summary>
    public bool ShowSearch { get; set; } = true;

    /// <summary>
    /// Invoked when the dropdown closes for any reason
    /// </summary>
    public Func<Task> OnClose { get; set; }
}
