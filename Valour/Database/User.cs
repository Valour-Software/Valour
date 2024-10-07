using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("users")]
public class User : Model, ISharedUser
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [InverseProperty("User")]
    public virtual UserPrivateInfo Email { get; set; }

    [InverseProperty("User")]
    public virtual ICollection<PlanetMember> Membership { get; set; }
    
    /// <summary>
    /// True if the user has a custom profile picture
    /// </summary>
    [Column("custom_avatar")]
    public bool HasCustomAvatar { get; set; }
    
    /// <summary>
    /// True if the user has an animated profile picture
    /// </summary>
    [Column("animated_avatar")]
    public bool HasAnimatedAvatar { get; set; }
    
    /// <summary>
    /// Old avatar url. Do not use.
    /// </summary>
    [Column("pfp_url")]
    public string OldAvatarUrl { get; set; }

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
    
    /// <summary>
    /// The tag (discriminator) of this user
    /// </summary>
    [Column("tag")]
    public string Tag { get; set; }
    
    /// <summary>
    /// If the user has completed the compliance step for regulatory purposes.
    /// This should only ever be false on legacy or testing accounts.
    /// </summary>
    [Column("compliance")]
    public bool Compliance { get; set; }
    
    /// <summary>
    /// If not null, the type of UserSubscription the user currently
    /// is subscribed to
    /// </summary>
    [Column("subscription_type")]
    public string SubscriptionType { get; set; }
    
    public string GetAvatarUrl(AvatarFormat format = AvatarFormat.Webp256) =>
        ISharedUser.GetAvatarUrl(this, format);
}

