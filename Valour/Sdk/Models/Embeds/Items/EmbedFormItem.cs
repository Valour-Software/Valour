using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

/// <summary>
/// A container whose input values are collected and sent to the bot
/// when a submit button inside it is clicked.
/// </summary>
public class EmbedFormItem : EmbedItem
{
    public const int MaxInputValueLength = 4096;

    public List<EmbedItem> Children { get; set; } = new();

    [JsonIgnore]
    public override EmbedItemType ItemType => EmbedItemType.Form;

    public override IEnumerable<EmbedItem> EnumerateDescendants() => EnumerateList(Children);

    public override bool TryReplaceDescendant(EmbedItem replacement) => TryReplaceInList(Children, replacement);

    /// <summary>
    /// Collects the values of all inputs in this form. Inputs without
    /// an Id are skipped; values are truncated to <see cref="MaxInputValueLength"/> chars.
    /// </summary>
    public List<EmbedFormData> GetFormData()
    {
        List<EmbedFormData> data = new();

        foreach (var item in EnumerateDescendants())
        {
            if (item is not IFormInputItem input || input.Id is null)
                continue;

            var value = input.Value;
            if (value is not null && value.Length > MaxInputValueLength)
                value = value[..MaxInputValueLength];

            data.Add(new EmbedFormData
            {
                ElementId = input.Id,
                Value = value,
                Type = input.ItemType,
            });
        }

        return data;
    }
}
