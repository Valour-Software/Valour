using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database;

public class UpdateAccessResult
{
    [Column("value")]
    public int Value { get; set; }
    
    [Column("planet_id")]
    public long PlanetId { get; set; }
}