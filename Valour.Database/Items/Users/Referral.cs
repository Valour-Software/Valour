using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database.Items.Users;

public class Referral : Valour.Shared.Users.Referral
{
    [ForeignKey("User_Id")]
    public virtual ServerUser User { get; set; }

    [ForeignKey("Referrer_Id")]
    public virtual ServerUser Referrer { get; set; }
}
