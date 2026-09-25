using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A short-lived request to add a new device to an account. The server relays
/// device keys, the approval, and the approving device's pins between the new
/// and existing device; the devices confirm each other with a secret one shows
/// and the other reads, which the server never sees.
/// </summary>
public class E2eeDeviceLinkSession
{
    public string Id { get; set; }
    public long UserId { get; set; }
    public int Mode { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public string DeviceId { get; set; }
    public byte[] DeviceSignPublicKey { get; set; }
    public byte[] DeviceEncryptPublicKey { get; set; }
    public string DeviceName { get; set; }
    public byte[] DeviceJoinProof { get; set; }

    /// <summary>
    /// When the existing device shows the code, the new device's proof that it
    /// scanned the code's secret.
    /// </summary>
    public byte[] JoinMac { get; set; }

    public string ApprovedByDeviceId { get; set; }

    /// <summary>
    /// The approving device's proof that it read or showed the link secret.
    /// The server cannot check it; the new device does.
    /// </summary>
    public byte[] ApprovalMac { get; set; }

    /// <summary>The approving device's pins, sealed to the new device.</summary>
    public byte[] ApprovalPins { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<E2eeDeviceLinkSession>(e =>
        {
            e.ToTable("e2ee_device_link_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.Mode).HasColumnName("mode");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.DeviceId).HasColumnName("device_id").HasMaxLength(64);
            e.Property(x => x.DeviceSignPublicKey).HasColumnName("device_sign_public_key");
            e.Property(x => x.DeviceEncryptPublicKey).HasColumnName("device_encrypt_public_key");
            e.Property(x => x.DeviceName).HasColumnName("device_name").HasMaxLength(64);
            e.Property(x => x.DeviceJoinProof).HasColumnName("device_join_proof");
            e.Property(x => x.JoinMac).HasColumnName("join_mac");
            e.Property(x => x.ApprovedByDeviceId).HasColumnName("approved_by_device_id").HasMaxLength(64);
            e.Property(x => x.ApprovalMac).HasColumnName("approval_mac");
            e.Property(x => x.ApprovalPins).HasColumnName("approval_pins");
            e.HasIndex(x => x.UserId);
        });
    }
}
