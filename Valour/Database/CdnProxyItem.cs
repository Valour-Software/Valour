using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

[Table("cdn_proxies")]
public class CdnProxyItem
{
    /// <summary>
    /// The id of proxied items are sha256 hashes of the original url
    /// </summary>
    [Key]
    [Column("id")]
    public string Id { get; set; }

    /// <summary>
    /// The original url fed to the proxy server
    /// </summary>
    [Column("origin")]
    public string Origin { get; set; }

    /// <summary>
    /// The type of content at the origin
    /// </summary>
    [Column("mime_type")]
    public string MimeType { get; set; }
    
    /// <summary>
    /// The width (if this is an image)
    /// </summary>
    [Column("width")]
    public int? Width { get; set; }
    
    /// <summary>
    /// The height (if this is an image)
    /// </summary>
    [Column("height")]
    public int? Height { get; set; }

    /// <summary>
    /// The url for the proxied item
    /// </summary>
    [JsonIgnore]
    public string Url => $"https://cdn.valour.gg/proxy/{Id}";

    public static void SetUpDbmodel(ModelBuilder builder)
    {
        builder.Entity<CdnProxyItem>(e =>
        {
            // ToTable
            e.ToTable("cdn_proxies");
            
            // Key
            e.HasKey(x => x.Id);
            
            // Properties
            e.Property(x => x.Origin)
                .HasColumnName("origin");
            
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.MimeType)
                .HasColumnName("mime_type");
            
            e.Property(x => x.Width)
                .HasColumnName("width");
            
            e.Property(x => x.Height)
                .HasColumnName("height");
            
            // Relationships
            
            // Indices

            e.HasIndex(x => x.Id);
        });
    }
}

