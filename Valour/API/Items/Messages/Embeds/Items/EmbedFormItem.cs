using System.Text.Json.Nodes;

namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedFormItem : EmbedItem
{
    /// <summary>
    /// The embed items in this form
    /// </summary>
    public List<EmbedItem>? Items { get; set; }

    public List<EmbedRow>? Rows { get; set; }

    /// <summary>
    /// the id of this form, ex "UserForms.User-Signup"
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Only works in FreelyBased embed/form type
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Only works in FreelyBased embed/form type
    /// </summary>
    public int? Height { get; set; }

    public EmbedItemPlacementType ItemPlacementType { get; set; }

    public EmbedFormItem()
    {
        ItemType = EmbedItemType.Form;    
    }

    public EmbedFormItem(EmbedItemPlacementType type, string id, int? width = null, int? height = null)
    {
        Id = id;
        ItemPlacementType = type;
        ItemType = EmbedItemType.Form;
        Width = width;
        Height = height;
    }

    public EmbedItem GetLastItem()
    {
        if (ItemPlacementType == EmbedItemPlacementType.RowBased) {
            return Rows.Last().Items.Last();
        }
        else {
            return Items.Last();
        }
    }

    public List<EmbedFormData> GetFormData()
    {
        List<EmbedFormData> data = new();

        IEnumerable<EmbedItem> items = null;
        if (ItemPlacementType == EmbedItemPlacementType.RowBased)
            items = Rows.SelectMany(x => x.Items);
        else
            items = Items;
        items = items.Where(x => x is IEmbedFormItem);
        foreach (IEmbedFormItem item in items)
        {
            if (item.ItemType == EmbedItemType.InputBox)
            {
                EmbedFormData DataItem = new()
                {
                    ElementId = item.Id,
                    Value = item.Value,
                    Type = item.ItemType
                };

                // do only if input is not null
                // limit the size of the input to 4096 char
                if (DataItem.Value != null)
                {
                    if (DataItem.Value.Length > 4096)
                    {
                        DataItem.Value = DataItem.Value.Substring(0, 4095);
                    }
                }
                data.Add(DataItem);
            }
            if (item.ItemType == EmbedItemType.DropDownMenu)
            {
				EmbedFormData DataItem = new()
				{
					ElementId = item.Id,
					Value = item.Value,
					Type = item.ItemType
				};

				data.Add(DataItem);
			}
        }
        return data;
    }

    public EmbedFormItem(JsonNode Node, Embed embed)
    {
        ItemType = EmbedItemType.Form;
        Id = (string)Node["Id"];
        ItemPlacementType = (EmbedItemPlacementType)(int)Node["ItemPlacementType"];
        if (ItemPlacementType == EmbedItemPlacementType.FreelyBased)
        {
            Width = (int?)Node["Width"];
            Height = (int?)Node["Height"];
        }
        Rows = new();
        switch (ItemPlacementType)
        {
            case EmbedItemPlacementType.FreelyBased:
                if (Node["Items"] is not null)
                {
                    Items = new();
                    foreach (JsonNode node in Node["Items"].AsArray())
                    {
                        Items.Add(Embed.ConvertNodeToEmbedItem(node, embed));
                    }
                }
                break;
            case EmbedItemPlacementType.RowBased:
                if (Node["Rows"] is not null && Node["Items"] is null)
                {
                    foreach (var rownode in Node["Rows"].AsArray())
                    {
                        EmbedRow rowobject = new();
                        if (rownode["Align"] is not null)
                            rowobject.Align = (EmbedAlignType)(int)rownode["Align"];
                        int i = 0;
                        foreach (JsonNode node in rownode["Items"].AsArray())
                        {
                            rowobject.Items.Add(Embed.ConvertNodeToEmbedItem(node, embed));
                        }
                        Rows.Add(rowobject);
                    }
                }
                break;
        }
    }

    public void AddItem(EmbedItem item)
    {
        if (ItemPlacementType == EmbedItemPlacementType.RowBased)
            Rows.Last().Items.Add(item);
        else
            Items.Add(item);
    }
}