using Valour.Sdk.Models.Embeds.Items;

namespace Valour.Sdk.Models.Embeds;

/// <summary>
/// The submitted value of a single form input.
/// </summary>
public class EmbedFormData
{
    public string? ElementId { get; set; }

    public string? Value { get; set; }

    public EmbedItemType Type { get; set; }
}
