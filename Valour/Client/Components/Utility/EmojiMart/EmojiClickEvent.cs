using System.Text.Json.Serialization;

namespace Valour.Client.Components.Utility.EmojiMart;

public class EmojiClickEvent
{
    [JsonPropertyName("aliases")]
    public string[] Aliases { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("keywords")]
    public string[] Keywords { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("native")]
    public string Native { get; set; }

    [JsonPropertyName("unified")]
    public string Unified { get; set; }

    [JsonPropertyName("shortcodes")]
    public string Shortcodes { get; set; }

    [JsonPropertyName("isCustom")]
    public bool IsCustom { get; set; }

    [JsonPropertyName("customId")]
    public long? CustomId { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; }

    [JsonPropertyName("src")]
    public string Src { get; set; }
}
