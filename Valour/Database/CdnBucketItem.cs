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

    /// <summary>Where the file is in its copy to the owner's Google Drive.</summary>
    public DriveSyncState DriveState { get; set; }

    /// <summary>The file's ID in the owner's Drive, once copied.</summary>
    public string DriveFileId { get; set; }

    public DateTime? DriveSyncedAt { get; set; }

    /// <summary>Copy attempts so far, for backing off after a failure.</summary>
    public int DriveAttempts { get; set; }

    /// <summary>The earliest time the next copy attempt may run.</summary>
    public DateTime? DriveNextAttemptAt { get; set; }

    /// <summary>A short, safe reason the last attempt failed.</summary>
    public string DriveError { get; set; }

    /// <summary>
    /// Set when the owner asked to send this file to Drive. Valour's copy is then
    /// removed as soon as the Drive copy exists, instead of after the retention days.
    /// </summary>
    public bool DriveReleaseOnSync { get; set; }

    /// <summary>
    /// When the copy on Valour's storage may be removed. Set once the Drive
    /// copy exists. Null means the Valour copy is kept.
    /// </summary>
    public DateTime? StorageExpiresAt { get; set; }

    /// <summary>
    /// When the copy on Valour's storage was removed. The file is then read
    /// from the owner's Drive. Files in this state do not count toward the
    /// account's storage quota.
    /// </summary>
    public DateTime? StoragePurgedAt { get; set; }

    /// <summary>
    /// The url for the bucket item
    /// </summary>
    [JsonIgnore]
    public string Url => $"{ValourHosts.ContentCdnBaseUrl}/content/{Category}/{UserId}/{Hash}";
    
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

            e.Property(x => x.DriveState)
                .HasColumnName("drive_state")
                .HasDefaultValue(DriveSyncState.None)
                .IsRequired();

            e.Property(x => x.DriveFileId)
                .HasColumnName("drive_file_id");

            e.Property(x => x.DriveSyncedAt)
                .HasColumnName("drive_synced_at");

            e.Property(x => x.DriveAttempts)
                .HasColumnName("drive_attempts")
                .HasDefaultValue(0)
                .IsRequired();

            e.Property(x => x.DriveNextAttemptAt)
                .HasColumnName("drive_next_attempt_at");

            e.Property(x => x.DriveError)
                .HasColumnName("drive_error");

            e.Property(x => x.DriveReleaseOnSync)
                .HasColumnName("drive_release_on_sync")
                .HasDefaultValue(false)
                .IsRequired();

            e.Property(x => x.StorageExpiresAt)
                .HasColumnName("storage_expires_at");

            e.Property(x => x.StoragePurgedAt)
                .HasColumnName("storage_purged_at");

            // The workers only look at files that are waiting or due to expire.
            e.HasIndex(x => x.DriveState)
                .HasFilter("drive_state IN (1, 2, 4)");
            e.HasIndex(x => x.StorageExpiresAt)
                .HasFilter("storage_expires_at IS NOT NULL AND storage_purged_at IS NULL");

            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.Hash);
            e.HasIndex(x => x.Sha256Hash);
            e.HasIndex(x => x.SafetyHashMatchState);
        });
    }
}
