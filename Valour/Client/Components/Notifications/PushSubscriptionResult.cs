#nullable  enable

using System.Text.Json.Serialization;

namespace Valour.Client.Components.Notifications;

public class PushSubscriptionDetails
{
    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; set; }
    
    [JsonPropertyName("key")]
    public required string Key { get; set; }
    
    [JsonPropertyName("auth")]
    public required string Auth { get; set; }
}

public class PushSubscriptionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("error")]
    public string? Error { get; set; }
    
    [JsonPropertyName("subscription")]
    public PushSubscriptionDetails? Subscription { get; set; }
}