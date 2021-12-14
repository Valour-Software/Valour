using System.Text.Json.Serialization;
using Valour.Shared.Items.Users;

namespace Valour.Api.Items.Users;

public class Referral : ISharedReferral
{
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    [JsonPropertyName("Referrer_Id")]
    public ulong Referrer_Id { get; set; }
}

