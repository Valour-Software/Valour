using Microsoft.AspNetCore.Components;

namespace Valour.Client.Utility;

public class ColumnDefinition<TModel>
{
    public string Name { get; set; }
    public RenderFragment<TModel> RenderFragment { get; set; }
}