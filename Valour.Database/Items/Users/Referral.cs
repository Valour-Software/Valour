using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database.Items.Users;

public class Referral : Valour.Shared.Items.Users.Referral
{
    [ForeignKey("User_Id")]
    public virtual User User { get; set; }

    [ForeignKey("Referrer_Id")]
    public virtual User Referrer { get; set; }
}
