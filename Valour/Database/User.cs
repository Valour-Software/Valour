using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Items.Users;

namespace Valour.Database;

[Table("users")]
public class User : Item, ISharedUser
{
    [InverseProperty("User")]
    public virtual UserEmail Email { get; set; }

    [InverseProperty("User")]
    public virtual ICollection<PlanetMember> Membership { get; set; }

    /// <summary>
    /// The url for the user's profile picture
    /// </summary>
    [Column("pfp_url")]
    public string PfpUrl { get; set; }

    /// <summary>
    /// The Date and Time that the user joined Valour
    /// </summary>
    [Column("time_joined")]
    public DateTime TimeJoined { get; set; }

    /// <summary>
    /// The name of this user
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// True if the user is a bot
    /// </summary>
    [Column("bot")]
    public bool Bot { get; set; }

    /// <summary>
    /// True if the account has been disabled
    /// </summary>
    [Column("disabled")]
    public bool Disabled { get; set; }

    /// <summary>
    /// True if this user is a member of the Valour official staff team. Falsely modifying this 
    /// through a client modification to present non-official staff as staff is a breach of our
    /// license. Don't do that.
    /// </summary>
    [Column("valour_staff")]
    public bool ValourStaff { get; set; }

    /// <summary>
    /// The user's currently set status - this could represent how they feel, their disdain for the political climate
    /// of the modern world, their love for their mother's cooking, or their hate for lazy programmers.
    /// </summary>
    [Column("status")]
    public string Status { get; set; }

    /// <summary>
    /// The integer representation of the current user state
    /// </summary>
    [Column("user_state_code")]
    public int UserStateCode { get; set; }

    /// <summary>
    /// The last time this user was flagged as active (successful auth)
    /// </summary>
    [Column("time_last_active")]
    public DateTime TimeLastActive { get; set; }
    
    /// <summary>
    /// True if the user has been recently on a mobile device
    /// </summary>
    [Column("is_mobile")]
    public bool IsMobile { get; set; }
}

