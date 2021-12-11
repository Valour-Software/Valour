using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Authorization;

/// <summary>
/// The object for storing a token locally
/// </summary>
public class LocalToken
{
    [JsonPropertyName("Token")]
    public string Token { get; set; }
}

