using System.ComponentModel.DataAnnotations;
using Valour.Shared.Items.Users;


namespace Valour.Database;

[Table("referrals")]
public class Referral : ISharedReferral
{
    [Key, Column("user_id")]
    public long UserId { get; set; }

    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }

    [ForeignKey("ReferrerId")]
    [JsonIgnore]
    public virtual User Referrer { get; set; }

    [Column("referrer_id")]
    public long ReferrerId { get; set; }
}

