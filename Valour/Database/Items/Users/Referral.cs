using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Users;

namespace Valour.Database.Items.Users;

public class Referral : Item, ISharedReferral
{
    [ForeignKey("User_Id")]
    public virtual User User { get; set; }

    [ForeignKey("Referrer_Id")]
    public virtual User Referrer { get; set; }

    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    [JsonPropertyName("Referrer_Id")]
    public ulong Referrer_Id { get; set; }

    [NotMapped]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.Referral;
}
