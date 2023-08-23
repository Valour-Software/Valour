using System.Text.Json.Serialization;

namespace Valour.Shared.Models;

public class OembedData
{
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    [JsonPropertyName("version")]
    public string Version { get; set; }
    
    [JsonPropertyName("width")]
    public int? Width { get; set; }
    
    [JsonPropertyName("height")]
    public int? Height { get; set; }
    
    [JsonPropertyName("title")]
    public string Title { get; set; }
    
    [JsonPropertyName("url")]
    public string Url { get; set; }
    
    [JsonPropertyName("author_name")]
    public string AuthorName { get; set; }
    
    [JsonPropertyName("author_url")]
    public string AuthorUrl { get; set; }
    
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; }
    
    [JsonPropertyName("provider_url")]
    public string ProviderUrl { get; set; }
    
    [JsonPropertyName("cache_age")]
    public string CacheAge { get; set; }
    
    [JsonPropertyName("html")]
    public string Html { get; set; }
}