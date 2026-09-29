using System.Text.Json.Serialization;

namespace Valour.Shared.Models;

/// <summary>
/// Metadata read from a web page for its link preview card. The server builds
/// it (see SitePreviewFetcher); clients never supply it. Text fields are
/// length-capped and cleaned, and Image and Icon are content CDN addresses
/// of images the server checked, not the page's own URLs.
/// </summary>
public class OpenGraphData
{
    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("image")]
    public string Image { get; set; }

    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; set; }

    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; set; }

    [JsonPropertyName("imageAlt")]
    public string ImageAlt { get; set; }

    /// <summary>
    /// The page's own site icon, when it offers one the server could read.
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; }

    /// <summary>
    /// The page's requested card style: "summary_large_image" asks for a large
    /// image, anything else for a small one.
    /// </summary>
    [JsonPropertyName("card")]
    public string Card { get; set; }

    /// <summary>
    /// The link the preview was made from, without its fragment.
    /// </summary>
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
