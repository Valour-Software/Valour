using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Database.Themes;

[Table("theme_assets")]
public class ThemeAsset
{
    [Column("id")]
    public long Id { get; set; }

    [Column("theme_id")]
    public long ThemeId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("animated")]
    public bool Animated { get; set; }

    /// <summary>
    /// The file extension used on the CDN (e.g. "webp", "gif")
    /// </summary>
    [Column("cdn_ext")]
    public string CdnExtension { get; set; } = "webp";

    /// <summary>
    /// The type of asset: "image" or "font"
    /// </summary>
    [Column("asset_type")]
    public string AssetType { get; set; } = "image";

    [ForeignKey("ThemeId")]
    [JsonIgnore]
    public virtual Theme Theme { get; set; }
}
