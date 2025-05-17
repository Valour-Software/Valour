using Microsoft.AspNetCore.Components;
using Valour.Client.Components.Utility;

namespace Valour.Client.Utility;

public class RowData<TModel>
{
    public QueryTable<TModel> Table { get; set; }
    public TModel Row { get; set; }
}

public class ColumnDefinition<TModel>
{
    public string Name { get; set; }
    public string SortField { get; set; }
    public RenderFragment<RowData<TModel>> RenderFragment { get; set; }
}