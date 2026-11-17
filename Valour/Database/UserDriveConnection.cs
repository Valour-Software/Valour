using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// A Google Drive an account lets Valour copy its attachments to. The refresh
/// token is encrypted with ASP.NET Data Protection. Valour only holds the
/// narrow drive.file permission, which reaches files and folders Valour made.
/// </summary>
public class UserDriveConnection
{
    public long UserId { get; set; }

    /// <summary>Google's account ID (<c>sub</c>), so reconnecting the same Drive is recognized.</summary>
    public string GoogleAccountId { get; set; }

    /// <summary>The Google email, shown so people know which Drive is connected.</summary>
    public string Email { get; set; }

    public string RefreshTokenEncrypted { get; set; }

    public DriveConnectionStatus Status { get; set; }

    public DateTime ConnectedAt { get; set; }

    /// <summary>
    /// Files uploaded from this moment on are sent to Drive automatically. Null
    /// means new uploads stay on Valour, which the person chose in settings.
    /// </summary>
    public DateTime? AutoSyncFrom { get; set; }

    public DateTime? LastSyncAt { get; set; }

    /// <summary>A short, safe reason the last copy or refresh failed.</summary>
    public string LastError { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<UserDriveConnection>(e =>
        {
            e.ToTable("user_drive_connections");

            e.HasKey(x => x.UserId);

            e.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();
            e.Property(x => x.GoogleAccountId).HasColumnName("google_account_id").IsRequired();
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.RefreshTokenEncrypted).HasColumnName("refresh_token_encrypted").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").IsRequired();
            e.Property(x => x.ConnectedAt).HasColumnName("connected_at").IsRequired();
            e.Property(x => x.AutoSyncFrom).HasColumnName("auto_sync_from");
            e.Property(x => x.LastSyncAt).HasColumnName("last_sync_at");
            e.Property(x => x.LastError).HasColumnName("last_error");
        });
    }
}
