using System.Text.Json.Serialization;

namespace Valour.Server.Nodes;

/// <summary>
/// Configuration for node system
/// </summary>
public class NodeConfig
{
    public static NodeConfig Instance;

    [JsonInclude]
    [JsonPropertyName("api_key")]
    public string API_Key { get; set; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    public NodeConfig()
    {
        Instance = this;
    }
}
