using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Items.Users;


namespace Valour.Database;

[Table("referrals")]
public class Referral : ISharedReferral
{
    [ForeignKey("UserId")]
    public virtual User User { get; set; }

    [ForeignKey("ReferrerId")]
    public virtual User Referrer { get; set; }
    
    [Key] 
    [Column("user_id")]
    public long UserId { get; set; }

    [Column("referrer_id")]
    public long ReferrerId { get; set; }
}

