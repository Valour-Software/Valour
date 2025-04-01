using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;


namespace Valour.Database;

public class Referral : ISharedReferral
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public virtual User User { get; set; }

    public virtual User Referrer { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long UserId { get; set; }

    public long ReferrerId { get; set; }

    public DateTime Created { get; set; }

    public decimal Reward { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<Referral>(e => 
            {
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
                
                // Relationships

                e.HasOne(x => x.User)
                    .WithOne(x => x.ReferredBy)
                    .HasForeignKey<Referral>(x => x.UserId);
                
                e.HasOne(x => x.Referrer)
                    .WithMany(x => x.Referrals)
                    .HasForeignKey(x => x.ReferrerId);
                
                // Indices
                
                e.HasIndex(x => x.UserId);
                
                e.HasIndex(x => x.ReferrerId);
                
            }
        );
    }
}