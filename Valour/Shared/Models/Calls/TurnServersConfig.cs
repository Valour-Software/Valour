using System.Text.Json.Serialization;

namespace Valour.Shared.Models.Calls;

public class IceServers
{
    [JsonPropertyName("urls")]
    public string[] Urls { get; set; }
    
    [JsonPropertyName("username")]
    public string Username { get; set; }
    
    [JsonPropertyName("credential")]
    public string Credential { get; set; }
}

public class TurnServersConfig
{
    [JsonPropertyName("iceServers")]
    public IceServers IceServers { get; set; }
}