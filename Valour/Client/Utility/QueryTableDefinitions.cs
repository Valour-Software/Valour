using Microsoft.AspNetCore.Components;
using Valour.Client.Components.Utility;
using Valour.Sdk.ModelLogic;

namespace Valour.Client.Utility;

public class RowData<TModel>
    where TModel : ClientModel<TModel>
{
    public QueryTable<TModel> Table { get; set; }
    public TModel Row { get; set; }
}

public class ColumnDefinition<TModel>
    where TModel : ClientModel<TModel>
{
    public string Name { get; set; }
    public string SortField { get; set; }
    public bool Sortable { get; set; } = false;
    public string TextAlign { get; set; } = "left";
    public string Width { get; set; } = "auto";
    public RenderFragment<RowData<TModel>> RenderFragment { get; set; }
}