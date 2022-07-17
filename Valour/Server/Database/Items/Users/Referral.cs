using Valour.Shared.Items.Users;

namespace Valour.Server.Database.Items.Users
{
    [Keyless]
    [Table("referrals")]
    public class Referral :  ISharedReferral
    {
        [ForeignKey("UserId")]
        [JsonIgnore]
        public virtual User User { get; set; }

        [ForeignKey("ReferrerId")]
        [JsonIgnore]
        public virtual User Referrer { get; set; }

        [Column("user_id")]
        public long UserId { get; set; }

        [Column("referrer_id")]
        public long ReferrerId { get; set; }
    }
}
