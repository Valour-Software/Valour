using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public enum ContentCategory
{
    Unknown = 0,
    Audio = 1,
    Image = 2,
    File = 3,
    Video = 4
}

public class CdnBucketItem
{
    public string Id { get; set; }
    public string Hash { get; set; }
    public long UserId { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }
    public ContentCategory Category { get; set; }
    public int SizeBytes { get; set; } 
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The url for the bucket item
    /// </summary>
    [JsonIgnore]
    public string Url => $"https://vmps.valour.gg/content/{Category}/{UserId}/{Hash}";
    
    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<CdnBucketItem>(e =>
        {
            // Table
            e.ToTable("cdn_bucket_items");

            // Keys
            e.HasKey(x => x.Id);

            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id")
                .IsRequired();
            
            e.Property(x => x.Hash)
                .HasColumnName("hash")
                .IsRequired();
            
            e.Property(x => x.UserId)
                .HasColumnName("user_id")
                .IsRequired();
            
            e.Property(x => x.MimeType)
                .HasColumnName("mime_type")
                .IsRequired();
            
            e.Property(x => x.FileName)
                .HasColumnName("file_name")
                .IsRequired();
            
            e.Property(x => x.Category)
                .HasColumnName("category")
                .IsRequired();
            
            e.Property(x => x.SizeBytes)
                .HasColumnName("size_bytes")
                .IsRequired();
            
            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();
        });
    }
}
