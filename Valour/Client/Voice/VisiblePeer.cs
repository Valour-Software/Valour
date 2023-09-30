using System.Text.Json.Serialization;

namespace Valour.Client.Voice;

public class VisiblePeer
{
    // Converted from json
    
    [JsonPropertyName("codec")]
    public string Codec { get; set; }
    
    [JsonPropertyName("id")]
    public string PeerId { get; set; }
    
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    [JsonPropertyName("kind")]
    public string Kind { get; set; }
    
    // Dotnet only
    
    public string ElementId { get; set; }
}