using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database;

/// <summary>
/// Used to block malicious email hosts
/// </summary>
[Table("blocked_user_emails")]
public class BlockedUserEmail
{
    [Key]
    [Column("email")]
    public string Email { get; set; }
}
