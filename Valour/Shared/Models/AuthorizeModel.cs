using System.Text.Json.Serialization;

namespace Valour.Shared.Models;

public class AuthorizeModel {
    [JsonPropertyName("client_id")]
    public long ClientId { get; set; }

    [JsonPropertyName("redirect_uri")]
    public string RedirectUri { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("response_type")]
    public string ResponseType { get; set; }

    [JsonPropertyName("scope")]
    public long Scope { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; }
}