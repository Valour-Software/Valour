using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// A provider-neutral GIF favorite. Legacy Tenor favorites remain in their
/// original table so the 0.7 migration never destroys user data.
/// </summary>
[Table("gif_favorites")]
[Index(nameof(UserId), nameof(Provider), nameof(ProviderId), IsUnique = true)]
public class GifFavorite : ISharedGifFavorite
{
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("provider")]
    [Required]
    public string Provider { get; set; } = string.Empty;

    [Column("provider_id")]
    [Required]
    public string ProviderId { get; set; } = string.Empty;

    [Column("title")]
    [Required]
    public string Title { get; set; } = string.Empty;

    [Column("preview_url")]
    [Required]
    public string PreviewUrl { get; set; } = string.Empty;

    [Column("gif_url")]
    [Required]
    public string GifUrl { get; set; } = string.Empty;

    [Column("width")]
    public int Width { get; set; }

    [Column("height")]
    public int Height { get; set; }
}
