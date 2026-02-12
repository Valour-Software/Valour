using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database;

/// <summary>
/// Allows tracking of email verification codes
/// </summary>
[Table("email_confirm_codes")]
public class EmailConfirmCode
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The code for the email verification
    /// </summary>
    [Key]
    [Column("code")]
    public string Code { get; set; }

    /// <summary>
    /// The user this code is verifying
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }
}

