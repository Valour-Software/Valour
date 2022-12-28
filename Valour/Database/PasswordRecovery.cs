using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database;

/// <summary>
/// Used for password recovery
/// </summary>
[Table("password_recoveries")]
public class PasswordRecovery
{
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    [Key]
    [Column("code")]
    public string Code { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }
}

