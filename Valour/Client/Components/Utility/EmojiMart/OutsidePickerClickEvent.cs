using System.Text.Json.Serialization;

namespace Valour.Client.Components.Utility.EmojiMart;

public class OutsidePickerClickEvent
{
    [JsonPropertyName("target")]
    public string Target { get; set; }
}
