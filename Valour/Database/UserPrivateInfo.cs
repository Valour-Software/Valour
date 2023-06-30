using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("user_emails")]
public class UserPrivateInfo : ISharedUserPrivateInfo
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

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
    
    /// <summary>
    /// The date of birth of the user (for COPPA and GDPR compliance)
    /// </summary>
    [Column("birth_date")]
    public DateTime? BirthDate { get; set; }
    
    /// <summary>
    /// Locality is used for data localization and other compliance purposes
    /// </summary>
    [Column("locality")]
    public Locality? Locality { get; set; }
}

