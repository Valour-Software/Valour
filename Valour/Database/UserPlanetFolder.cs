using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public class UserPlanetFolder
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }

    public static void SetupDbModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserPlanetFolder>(e =>
        {
            e.ToTable("user_planet_folders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
            e.Property(x => x.Position).HasColumnName("position").IsRequired();
            e.HasIndex(x => new { x.UserId, x.Position });
        });
    }
}
