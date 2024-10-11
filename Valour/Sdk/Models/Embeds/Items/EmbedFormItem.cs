using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Items;

public class EmbedFormItem : EmbedItem
{
    /// <summary>
    /// the id of this form, ex "UserForms.User-Signup"
    /// </summary>
    public new string Id { get; set; }

	[JsonIgnore]
	public override EmbedItemType ItemType => EmbedItemType.Form;

	public override EmbedItem GetLastItem(bool InsideofForms)
    {
        if (InsideofForms)
            return Children.Last().GetLastItem(InsideofForms);
        else
            return this;
    }

    public List<EmbedFormData> GetFormData()
    {
        List<EmbedFormData> data = new();
        
        var items = GetAllItems().Where(x => x is IEmbedFormItem);
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
}