using System.ComponentModel.DataAnnotations;

namespace Valour.Server.Database.Items.Users;

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
