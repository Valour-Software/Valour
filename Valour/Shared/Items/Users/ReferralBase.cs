using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

public class ReferralBase : ISharedItem
{
    [JsonPropertyName("UserId")]
    public ulong UserId { get; set; }

    [JsonPropertyName("ReferrerId")]
    public ulong ReferrerId { get; set; }

    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.Referral;
}

