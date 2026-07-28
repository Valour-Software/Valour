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

   /// <summary>
   /// True only for official seed tags; the onboarding interest picker shows
   /// nothing else. Never settable through the API.
   /// </summary>
   public bool Curated { get; set; }

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

           e.Property(t => t.Curated)
               .HasColumnName("curated")
               .HasDefaultValue(false)
               .IsRequired();
           
          
           
           
           
           
           // Use a fixed UTC date for seed data to avoid migration regeneration issues
           var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
           e.HasData(
               new PlanetTag { Id = 1, Name = "Gaming", Slug = "gaming", Created = seedDate, Curated = true },
               new PlanetTag { Id = 2, Name = "Anime", Slug = "anime", Created = seedDate, Curated = true },
               new PlanetTag { Id = 3, Name = "Debates", Slug = "debates", Created = seedDate, Curated = true },
               new PlanetTag { Id = 4, Name = "News", Slug = "news", Created = seedDate, Curated = true },
               new PlanetTag { Id = 5, Name = "Strategy", Slug = "strategy", Created = seedDate, Curated = true },
               new PlanetTag { Id = 6, Name = "Action", Slug = "action", Created = seedDate, Curated = true },
               new PlanetTag { Id = 7, Name = "Manga", Slug = "manga", Created = seedDate, Curated = true },
               new PlanetTag { Id = 8, Name = "Geek Culture", Slug = "geek-culture", Created = seedDate, Curated = true },
               new PlanetTag { Id = 9, Name = "Events", Slug = "events", Created = seedDate, Curated = true },
               new PlanetTag { Id = 10, Name = "Indie Games", Slug = "indie-games", Created = seedDate, Curated = true },
               new PlanetTag { Id = 11, Name = "Music", Slug = "music", Created = seedDate, Curated = true },
               new PlanetTag { Id = 12, Name = "Art", Slug = "art", Created = seedDate, Curated = true },
               new PlanetTag { Id = 13, Name = "Technology", Slug = "technology", Created = seedDate, Curated = true },
               new PlanetTag { Id = 14, Name = "Programming", Slug = "programming", Created = seedDate, Curated = true },
               new PlanetTag { Id = 15, Name = "Science", Slug = "science", Created = seedDate, Curated = true },
               new PlanetTag { Id = 16, Name = "Movies & TV", Slug = "movies-tv", Created = seedDate, Curated = true },
               new PlanetTag { Id = 17, Name = "Books & Writing", Slug = "books-writing", Created = seedDate, Curated = true },
               new PlanetTag { Id = 18, Name = "Memes", Slug = "memes", Created = seedDate, Curated = true },
               new PlanetTag { Id = 19, Name = "Sports", Slug = "sports", Created = seedDate, Curated = true },
               new PlanetTag { Id = 20, Name = "Fitness", Slug = "fitness", Created = seedDate, Curated = true },
               new PlanetTag { Id = 21, Name = "Food & Cooking", Slug = "food-cooking", Created = seedDate, Curated = true },
               new PlanetTag { Id = 22, Name = "Roleplay", Slug = "roleplay", Created = seedDate, Curated = true },
               new PlanetTag { Id = 23, Name = "Pets & Animals", Slug = "pets-animals", Created = seedDate, Curated = true },
               new PlanetTag { Id = 24, Name = "Education", Slug = "education", Created = seedDate, Curated = true },
               new PlanetTag { Id = 25, Name = "Travel", Slug = "travel", Created = seedDate, Curated = true }
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