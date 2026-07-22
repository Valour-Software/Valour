using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public sealed class PlatformBannerConfiguration
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool IsActive { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public int Kind { get; set; }
    public long UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlatformBannerConfiguration>(e =>
        {
            e.ToTable("platform_banner_configuration");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(80);
            e.Property(x => x.Message).HasColumnName("message").HasMaxLength(500);
            e.Property(x => x.Kind).HasColumnName("kind");
            e.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });
    }
}
