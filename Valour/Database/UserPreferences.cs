using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("user_preferences")]
public class UserPreferences : ISharedUserPreferences
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("error_reporting_state")]
    public ErrorReportingState ErrorReportingState { get; set; }
}
