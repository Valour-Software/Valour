using Valour.Shared.Models;

namespace Valour.Server.Models;

public class User : Item, ISharedUser
{
    /// <summary>
    /// True if the user has a custom profile picture
    /// </summary>
    public bool HasCustomAvatar { get; set; }
    
    /// <summary>
    /// True if the user has an animated profile picture
    /// </summary>
    public bool HasAnimatedAvatar { get; set; }

    /// <summary>
    /// The Date and Time that the user joined Valour
    /// </summary>
    public DateTime TimeJoined { get; set; }

    /// <summary>
    /// The name of this user
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The tag (discriminator) of this user
    /// </summary>
    public string Tag { get; set; }

    /// <summary>
    /// True if the user is a bot
    /// </summary>
    public bool Bot { get; set; }

    /// <summary>
    /// True if the account has been disabled
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// True if this user is a member of the Valour official staff team. Falsely modifying this 
    /// through a client modification to present non-official staff as staff is a breach of our
    /// license. Don't do that.
    /// </summary>
    public bool ValourStaff { get; set; }

    /// <summary>
    /// The user's currently set status - this could represent how they feel, their disdain for the political climate
    /// of the modern world, their love for their mother's cooking, or their hate for lazy programmers.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// The integer representation of the current user state
    /// </summary>
    public int UserStateCode { get; set; }

    /// <summary>
    /// The last time this user was flagged as active (successful auth)
    /// </summary>
    public DateTime TimeLastActive { get; set; }
    
    /// <summary>
    /// True if the user has been recently on a mobile device
    /// </summary>
    public bool IsMobile { get; set; }
    
    /// <summary>
    /// If the user has completed the compliance step for regulatory purposes.
    /// This should only ever be false on legacy or testing accounts.
    /// </summary>
    public bool Compliance { get; set; }
    
    /// <summary>
    /// If not null, the type of UserSubscription the user currently
    /// is subscribed to
    /// </summary>
    public string SubscriptionType { get; set; }
    
    /// <summary>
    /// The subscription the user currently has
    /// </summary>
    [JsonIgnore]
    public UserSubscriptionType Subscription =>
        ISharedUser.GetSubscription(this);

    /// <summary>
    /// The span of time from which the user was last active
    /// </summary>
    [JsonIgnore]
    public TimeSpan LastActiveSpan =>
        ISharedUser.GetLastActiveSpan(this);

    /// <summary>
    /// The current activity state of the user
    /// </summary>
    [JsonIgnore]
    public UserState UserState
    {
        get => ISharedUser.GetUserState(this);
        set => ISharedUser.SetUserState(this, value);
    }
    
    public string GetAvatarUrl(AvatarFormat format = AvatarFormat.Webp256) =>
        ISharedUser.GetAvatarUrl(this, format);
}