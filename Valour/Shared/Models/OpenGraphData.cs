using System.Text.Json.Serialization;

namespace Valour.Shared.Models;

/// <summary>
/// Represents Open Graph metadata extracted from a webpage for link previews
/// </summary>
public class OpenGraphData
{
    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("image")]
    public string Image { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("siteName")]
    public string SiteName { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    /// <summary>
    /// Whether this preview has enough data to be useful
    /// </summary>
    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Description);
}
