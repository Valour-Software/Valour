using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;


namespace Valour.Database;

[Table("referrals")]
public class Referral : ISharedReferral
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    [ForeignKey("UserId")] public virtual User User { get; set; }

    [ForeignKey("ReferrerId")] public virtual User Referrer { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    [Key] [Column("user_id")] public long UserId { get; set; }

    [Column("referrer_id")] public long ReferrerId { get; set; }

    [Column("created")] public DateTime Created { get; set; }

    [Column("reward")] public decimal Reward { get; set; }

    public static void SetUpDDModel(ModelBuilder builder)
    {
        builder.Entity<Referral>(e =>
            {
                // TOTable
                
                e.ToTable("referrals");
                
                // Key
                e.HasKey(x => x.UserId);
                
                // Properties

                e.Property(x => x.UserId)
                    .HasColumnName("user_id");
                
                e.Property(x => x.ReferrerId)
                    .HasColumnName("referrer_id");

                e.Property(x => x.Created)
                    .HasColumnName("created")
                    .HasConversion(
                        x => x,
                        x => new DateTime(x.Ticks, DateTimeKind.Utc)
                    );

                e.Property(x => x.Reward)
                    .HasColumnName("reward");
                
                // Relattionships

                e.HasOne(x => x.User)
                    .WithMany(x => x.Rewards)
                    .HasForeignKey(x => x.UserId);
                
                e.HasOne(x => x.Referrer)
                    .WithMany(x => x.Rewards)
                    .HasForeignKey(x => x.ReferrerId);
                
                // Indices
                
                e.HasIndex(x => x.UserId);
                
                e.HasIndex(x => x.ReferrerId);
                
            }
        );
    }

}

