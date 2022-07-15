namespace Valour.Server.Nodes;

/// <summary>
/// Configuration for node system
/// </summary>
public class NodeConfig
{
    public static NodeConfig Instance;

    [JsonInclude]
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonInclude]
    [JsonPropertyName("address")]
    public string Address { get; set; }

    public NodeConfig()
    {
        Instance = this;
    }
}
