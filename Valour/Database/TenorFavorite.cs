using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// Represents a favorite gif or media from Tenor
/// </summary>
[Table("tenor_favorites")]
public class TenorFavorite : Model, ISharedTenorFavorite
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Column("id")]
    public new long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }
    
    [Column("tenor_id")]
    public string TenorId { get; set; }
}