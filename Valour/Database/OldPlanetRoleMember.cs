using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public class OldPlanetRoleMember
{
    public virtual PlanetRole Role { get; set; }
    public virtual PlanetMember Member { get; set; }
    
    public long Id { get; set; }
    public long UserId { get; set; }
    public long RoleId { get; set; }
    public long MemberId { get; set; }
    public long PlanetId { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<OldPlanetRoleMember>(e =>
        {
            e.ToTable("planet_role_members");
            
            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x => x.RoleId)
                .HasColumnName("role_id");
            
            e.Property(x => x.MemberId)
                .HasColumnName("member_id");
            
            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");
            
            e.HasOne(x => x.Member)
                .WithMany(x => x.OldRoleMembers);

            e.HasOne(x => x.Role)
                .WithMany(x => x.OldRoleMembers);
        });
    }
}