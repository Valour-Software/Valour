using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

public interface ISharedReferral
{
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    [JsonPropertyName("Referrer_Id")]
    public ulong Referrer_Id { get; set; }
}

