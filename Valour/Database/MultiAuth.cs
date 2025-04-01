using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public class MultiAuth
{
    public virtual User User { get; set; }
    
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Type { get; set; }
    public string Secret { get; set; }
    public bool Verified { get; set; }

    public DateTime CreatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<MultiAuth>(e =>
        {
            e.ToTable("multi_auth");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.Type)
                .HasColumnName("type");

            e.Property(x => x.Secret)
                .HasColumnName("secret");

            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );
            
            e.Property(x => x.Verified)
                .HasColumnName("verified");

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId);

            e.HasIndex(x => x.UserId);
        });
    }
}