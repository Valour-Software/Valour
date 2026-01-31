using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class PlanetTag : ISharedPlanetTag
{
   ///////////////////////////
   // Relational Properties //
   ///////////////////////////
   public virtual ICollection<Planet> Planets { get; set; }
   
   
   ///////////////////////
   // Entity Properties //
   ///////////////////////
        
   /// <summary>
   /// The unique ID of the Tag.
   /// </summary>
   public long Id { get; set; }
   /// <summary>
   /// The tag name
   /// </summary>
   public string Name { get; set; }
   /// <summary>
   /// URL-friendly version ("game-dev" instead of "Game Dev")
   /// </summary>
   public string Slug { get; set; }
   /// <summary>
   /// Creation Date
   /// </summary>
   
   public DateTime Created { get; set; }

   public static void SetupDbModel(ModelBuilder builder)
   {
       builder.Entity<PlanetTag>(e =>
       {
           // Table
           e.ToTable("tags");

           // Keys
           e.HasKey(x => x.Id);
           
           // Properties
           e.Property(t=>t.Id)
               .HasColumnName("id");
           
           e.Property(t => t.Name)
               .HasColumnName("name");

           e.Property(t => t.Slug)
               .HasColumnName("slug")
               .HasMaxLength(20);

           e.Property(t => t.Created)
               .HasColumnName("created_date");
           
          
           
           
           
           
           // Use a fixed UTC date for seed data to avoid migration regeneration issues
           var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
           e.HasData(
               new PlanetTag { Id = 1, Name = "Gaming", Slug = "gaming", Created = seedDate },
               new PlanetTag { Id = 2, Name = "Anime", Slug = "anime", Created = seedDate },
               new PlanetTag { Id = 3, Name = "Debates", Slug = "debates", Created = seedDate },
               new PlanetTag { Id = 4, Name = "News", Slug = "news", Created = seedDate },
               new PlanetTag { Id = 5, Name = "Strategy", Slug = "strategy", Created = seedDate },
               new PlanetTag { Id = 6, Name = "Action", Slug = "action", Created = seedDate },
               new PlanetTag { Id = 7, Name = "Manga", Slug = "manga", Created = seedDate },
               new PlanetTag { Id = 8, Name = "Geek Culture", Slug = "geek-culture", Created = seedDate },
               new PlanetTag { Id = 9, Name = "Events", Slug = "events", Created = seedDate },
               new PlanetTag { Id = 10, Name = "Indie Games", Slug = "indie-games", Created = seedDate }
           );

           builder.Entity<PlanetTag>()
               .HasMany(t => t.Planets)
               .WithMany(p => p.Tags)
               .UsingEntity<Dictionary<string, object>>(
                   "planet_tags",
                   j => j
                       .HasOne<Planet>()
                       .WithMany()
                       .HasForeignKey("planet_id")
                       .HasConstraintName("fk_planet_tag_planet_id")
                       .OnDelete(DeleteBehavior.Cascade),
                   j => j
                       .HasOne<PlanetTag>()
                       .WithMany()
                       .HasForeignKey("tag_id")
                       .HasConstraintName("fk_planet_tag_tag_id")
                       .OnDelete(DeleteBehavior.Cascade),
                   j =>
                   {
                       j.HasKey("planet_id", "tag_id");
                       j.ToTable("planet_tags");
                   });

       });
       
       
   }
   
   
}