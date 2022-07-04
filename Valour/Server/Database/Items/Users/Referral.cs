namespace Valour.Server.Database.Items.Users
{
    [Keyless]
    [Table("referrals")]
    public class Referral : Shared.Items.Users.Referral
    {
        [ForeignKey("UserId")]
        [JsonIgnore]
        public virtual User User { get; set; }

        [ForeignKey("ReferrerId")]
        [JsonIgnore]
        public virtual User Referrer { get; set; }
    }
}
