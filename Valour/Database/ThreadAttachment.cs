using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class ThreadAttachment
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public virtual PlanetThread Thread { get; set; }
    public virtual CdnBucketItem CdnBucketItem { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long ThreadId { get; set; }
    public int SortOrder { get; set; }
    public MessageAttachmentType Type { get; set; }
    public string CdnBucketItemId { get; set; }
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Inline { get; set; }
    public bool Missing { get; set; }
    public string Data { get; set; }
    public string OpenGraphData { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<ThreadAttachment>(e =>
        {
            e.ToTable("thread_attachments");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.ThreadId)
                .HasColumnName("thread_id")
                .IsRequired();

            e.Property(x => x.SortOrder)
                .HasColumnName("sort_order")
                .IsRequired();

            e.Property(x => x.Type)
                .HasColumnName("type")
                .IsRequired();

            e.Property(x => x.CdnBucketItemId)
                .HasColumnName("cdn_bucket_item_id");

            e.Property(x => x.Location)
                .HasColumnName("location")
                .IsRequired();

            e.Property(x => x.MimeType)
                .HasColumnName("mime_type");

            e.Property(x => x.FileName)
                .HasColumnName("file_name");

            e.Property(x => x.Width)
                .HasColumnName("width")
                .IsRequired();

            e.Property(x => x.Height)
                .HasColumnName("height")
                .IsRequired();

            e.Property(x => x.Inline)
                .HasColumnName("inline")
                .IsRequired();

            e.Property(x => x.Missing)
                .HasColumnName("missing")
                .IsRequired();

            e.Property(x => x.Data)
                .HasColumnName("data");

            e.Property(x => x.OpenGraphData)
                .HasColumnName("open_graph_data");

            e.HasOne(x => x.Thread)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.CdnBucketItem)
                .WithMany()
                .HasForeignKey(x => x.CdnBucketItemId);

            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => x.CdnBucketItemId);
        });
    }
}
