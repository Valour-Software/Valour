using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;


namespace Valour.Database;

[Table("referrals")]
public class Referral : ISharedReferral
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Key] 
    [Column("user_id")]
    public long UserId { get; set; }

    [Column("referrer_id")]
    public long ReferrerId { get; set; }
    
    [Column("created")]
    public DateTime Created { get; set; }
    
    [Column("reward")]
    public decimal Reward { get; set; }
}

