using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

public class ReferralBase : Item
{
    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    [JsonPropertyName("Referrer_Id")]
    public ulong Referrer_Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.Referral;
}

