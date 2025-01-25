using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// Represents a favorite gif or media from Tenor
/// </summary>
[Table("tenor_favorites")]
public class TenorFavorite : ISharedTenorFavorite
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }
    
    [Column("tenor_id")]
    public string TenorId { get; set; }


    public static void SetUpDdModel(ModelBuilder builder)
    {
        builder.Entity<TenorFavorite>(e =>
        {
            // ToTable
            
            e.ToTable("tenor_favorites");
            
            // Key
            
            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");
            
            e.Property(x => x.TenorId)
                .HasColumnName("tenor_id");
            
            // Relationships 
            
            // Indices
            
            e.HasIndex(x => x.Id);   
            
            e.HasIndex(x => x.TenorId);
            
            e.HasIndex(x => x.UserId)
                .IsUnique();


        });
    }
}