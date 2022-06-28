using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Users;

public class Referral
{
    public ulong UserId { get; set; }
    public ulong ReferrerId { get; set; }

    [JsonInclude]
    public ItemType ItemType => ItemType.Referral;
}

