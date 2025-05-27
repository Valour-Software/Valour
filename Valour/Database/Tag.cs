using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public class Tag
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
       builder.Entity<Tag>(e =>
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
           
          
           
           
           
           
           e.HasData(
               new Tag { Id = 1, Name = "Gaming", Slug = "gaming",Created = DateTime.Today},
               new Tag { Id = 2, Name = "Anime", Slug = "anime",Created = DateTime.Today},
               new Tag { Id = 3, Name = "Debates", Slug = "debates",Created = DateTime.Today},
               new Tag { Id = 4, Name = "News", Slug = "news",Created = DateTime.Today},
               new Tag { Id = 5, Name = "Strategy", Slug = "strategy",Created = DateTime.Today},
               new Tag { Id = 6, Name = "Action", Slug = "action",Created = DateTime.Today},
               new Tag { Id = 7, Name = "Manga", Slug = "manga",Created = DateTime.Today},
               new Tag { Id = 8, Name = "Geek Culture", Slug = "geek-culture",Created = DateTime.Today},
               new Tag { Id = 9, Name = "Events", Slug = "events",Created = DateTime.Today},
               new Tag { Id = 10, Name = "Indie Games", Slug = "indie-games",Created = DateTime.Today}
           );

           builder.Entity<Tag>()
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
                       .HasOne<Tag>()
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