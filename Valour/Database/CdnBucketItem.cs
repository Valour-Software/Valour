using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Database;

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
    public string Sha256Hash { get; set; }
    public MediaSafetyHashMatchState SafetyHashMatchState { get; set; }
    public string SafetyProvider { get; set; }
    public DateTime? SafetyHashMatchedAt { get; set; }
    public string SafetyMatchId { get; set; }
    public string SafetyDetails { get; set; }
    public DateTime? SafetyQuarantinedAt { get; set; }

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

            e.Property(x => x.Sha256Hash)
                .HasColumnName("sha256_hash");

            e.Property(x => x.SafetyHashMatchState)
                .HasColumnName("safety_hash_match_state")
                .IsRequired();

            e.Property(x => x.SafetyProvider)
                .HasColumnName("safety_provider");

            e.Property(x => x.SafetyHashMatchedAt)
                .HasColumnName("safety_hash_matched_at");

            e.Property(x => x.SafetyMatchId)
                .HasColumnName("safety_match_id");

            e.Property(x => x.SafetyDetails)
                .HasColumnName("safety_details");

            e.Property(x => x.SafetyQuarantinedAt)
                .HasColumnName("safety_quarantined_at");

            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Hash);
            e.HasIndex(x => x.Sha256Hash);
            e.HasIndex(x => x.SafetyHashMatchState);
        });
    }
}
