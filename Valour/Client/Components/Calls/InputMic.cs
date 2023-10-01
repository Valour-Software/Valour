using System.Text.Json.Serialization;

namespace Valour.Client.Components.Calls;

public class InputMic
{
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; }
    
    [JsonPropertyName("label")]
    public string Label { get; set; }
}