using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database;

public class UpdateAccessRowCountResult
{
    [Column("value")]
    public int RowsUpdated { get; set; }
}