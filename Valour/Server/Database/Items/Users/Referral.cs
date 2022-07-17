using System.ComponentModel.DataAnnotations;
using Valour.Shared.Items.Users;

namespace Valour.Server.Database.Items.Users
{
    [Table("referrals")]
    public class Referral : ISharedReferral
    {
        [ForeignKey("UserId")]
        [JsonIgnore]
        public virtual User User { get; set; }

        [ForeignKey("ReferrerId")]
        [JsonIgnore]
        public virtual User Referrer { get; set; }

        [Key, Column("id")]
        public long Id { get; set; }

        [Column("user_id")]
        public long UserId { get; set; }

        [Column("referrer_id")]
        public long ReferrerId { get; set; }
    }
}
