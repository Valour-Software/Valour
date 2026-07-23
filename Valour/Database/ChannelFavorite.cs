using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// A channel a user has favorited, ordered per planet by Position.
/// Intentionally has no channel/planet foreign keys: favorites may reference
/// channels on federated planets that do not exist in the local database.
/// </summary>
[Table("channel_favorites")]
[Index(nameof(UserId), nameof(ChannelId), IsUnique = true)]
[Index(nameof(UserId))]
public class ChannelFavorite : ISharedChannelFavorite
{
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("channel_id")]
    public long ChannelId { get; set; }

    [Column("planet_id")]
    public long PlanetId { get; set; }

    [Column("position")]
    public int Position { get; set; }
}
