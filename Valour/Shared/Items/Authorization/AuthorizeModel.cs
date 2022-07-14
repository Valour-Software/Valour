using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Authorization;

public class AuthorizeModel {
    [JsonPropertyName("client_id")]
    public long client_id { get; set; }

    [JsonPropertyName("redirect_uri")]
    public string redirect_uri { get; set; }

    [JsonPropertyName("userId")]
    public long userId { get; set; }

    [JsonPropertyName("response_type")]
    public string response_type { get; set; }

    [JsonPropertyName("scope")]
    public long scope { get; set; }

    [JsonPropertyName("state")]
    public string state { get; set; }

    [JsonPropertyName("code")]
    public string code { get; set; }
}