using System.Text.Json.Nodes;

namespace Valour.Api.Items.Messages.Embeds.Items;

public class EmbedFormItem : EmbedItem
{
    /// <summary>
    /// The embed items in this form
    /// </summary>
    public List<EmbedItem> Items;

    public List<EmbedRow> Rows;

    /// <summary>
    /// the id of this form, ex "UserForms.User-Signup"
    /// </summary>
    public string Id { get; set; }

    public EmbedItemPlacementType ItemPlacementType { get; set; }

    public EmbedFormItem(EmbedItemPlacementType type, string id)
    {
        Id = id;
        ItemPlacementType = type;
    }
    public EmbedFormItem(JsonNode node)
    {
        
    }

    public void AddItem(EmbedItem item)
    {
        if (ItemPlacementType == EmbedItemPlacementType.RowBased)
            Rows.Last().Items.Add(item);
        else
            Items.Add(item);
    }
}