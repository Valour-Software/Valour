using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// A member waiting for a channel key. Online members who hold the key see
/// the request and seal the key for them.
/// </summary>
public class E2eeKeyRequest
{
    public long ChannelId { get; set; }
    public long UserId { get; set; }
    public DateTime RequestedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<E2eeKeyRequest>(e =>
        {
            e.ToTable("e2ee_key_requests");
            e.HasKey(x => new { x.ChannelId, x.UserId });
            e.Property(x => x.ChannelId).HasColumnName("channel_id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.RequestedAt).HasColumnName("requested_at");
        });
    }
}
