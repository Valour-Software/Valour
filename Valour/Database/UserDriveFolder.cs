using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A folder Valour made in an account's Google Drive. Folders are found by
/// the ID stored here, never by name, so a folder someone renames in Drive
/// keeps receiving files. The key says what the folder is for, such as
/// <c>root</c>, <c>planets</c>, <c>planet:123</c> or <c>planet:123:2026-11</c>.
/// </summary>
public class UserDriveFolder
{
    public long UserId { get; set; }

    public string Key { get; set; }

    public string DriveFolderId { get; set; }

    /// <summary>The name Valour last gave the folder, so a renamed planet renames its folder lazily.</summary>
    public string Name { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<UserDriveFolder>(e =>
        {
            e.ToTable("user_drive_folders");

            e.HasKey(x => new { x.UserId, x.Key });

            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Key).HasColumnName("key").HasMaxLength(128);
            e.Property(x => x.DriveFolderId).HasColumnName("drive_folder_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
        });
    }
}
