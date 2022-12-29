using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Database;

[Table("user_emails")]
public class UserEmail
{
    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }

    /// <summary>
    /// The user's email address
    /// </summary>
    [Key]
    [Column("email")]
    public string Email { get; set; }

    /// <summary>
    /// True if the email is verified
    /// </summary>
    [Column("verified")]
    public bool Verified { get; set; }

    /// <summary>
    /// The user this email belongs to
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }
}

