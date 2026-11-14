using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Frozen;
using System.Text.Json.Serialization;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Models;

public interface  ISharedUser : ISharedModel<long>
{
    const string BaseRoute = "api/users";
    public static string GetIdRoute(long id) => $"{BaseRoute}/{id}";
    
    const long VictorUserId = 20579262493097984;
    
    const int FLAGS_TIME_UPDATE = 0x01;
    
    const string TagChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    
    /// <summary>
    /// The maximum planets a user is allowed to have. This will increase after 
    /// the alpha period is complete.
    /// </summary>
    [JsonIgnore] 
    public const int MaxOwnedPlanets = 5;

    /// <summary>
    /// The maximum planets a user is allowed to join. This will increase after the 
    /// alpha period is complete.
    /// </summary>
    [JsonIgnore] 
    public const int MaxJoinedPlanets = 20;
    
    /// <summary>
    /// True if the user has a custom profile picture
    /// </summary>
    bool HasCustomAvatar { get; set; }
    
    /// <summary>
    /// True if the user has an animated profile picture
    /// </summary>
    bool HasAnimatedAvatar { get; set; }

    /// <summary>
    /// The Date and Time that the user joined Valour
    /// </summary>
    DateTime TimeJoined { get; set; }

    /// <summary>
    /// The name of this user
    /// </summary>
    string Name { get; set; }
    
    /// <summary>
    /// The tag (discriminator) of this user
    /// </summary>
    string Tag { get; set; }

    /// <summary>
    /// True if the user is a bot
    /// </summary>
    bool Bot { get; set; }

    /// <summary>
    /// True if the account has been disabled
    /// </summary>
    bool Disabled { get; set; }

    /// <summary>
    /// True if this user is a member of the Valour official staff team. Falsely modifying this 
    /// through a client modification to present non-official staff as staff is a breach of our
    /// license. Don't do that.
    /// </summary>
    bool ValourStaff { get; set; }

    /// <summary>
    /// The user's currently set status - this could represent how they feel, their disdain for the political climate
    /// of the modern world, their love for their mother's cooking, or their hate for lazy programmers.
    /// </summary>
    string Status { get; set; }

    /// <summary>
    /// The integer representation of the current user state
    /// </summary>
    int UserStateCode { get; set; }

    /// <summary>
    /// The last time this user was flagged as active (successful auth)
    /// </summary>
    DateTime TimeLastActive { get; set; }
    
    /// <summary>
    /// True if the user has been recently on a mobile device
    /// </summary>
    bool IsMobile { get; set; }
    
    /// <summary>
    /// If the user has completed the compliance step for regulatory purposes.
    /// This should only ever be false on legacy or testing accounts.
    /// </summary>
    bool Compliance { get; set; }

    /// <summary>
    /// If not null, the type of UserSubscription the user currently
    /// is subscribed to
    /// </summary>
    string SubscriptionType { get; set; }
    
    /// <summary>
    /// The user's prior username, if they have changed it before.
    /// </summary>
    public string PriorName { get; set; }

    /// <summary>
    /// When true, the user's prior username is hidden from other users.
    /// </summary>
    public bool HidePriorName { get; set; }

    /// <summary>
    /// The date and time the user last changed their username.
    /// </summary>
    public DateTime? NameChangeTime { get; set; }
    
    /// <summary>
    /// The version of the user. Used for cache busting.
    /// </summary>
    public int Version { get; set; }
    
    /// <summary>
    /// Bitmask representing tutorials the user has completed
    /// </summary>
    public long TutorialState { get; set; }

    /// <summary>
    /// If this user is a bot, the ID of the user who owns it.
    /// Null for regular users.
    /// </summary>
    public long? OwnerId { get; set; }

    /// <summary>
    /// The first gradient color for the stargazer badge.
    /// Only customizable by Stargazer Pro subscribers.
    /// </summary>
    public string StarColor1 { get; set; }

    /// <summary>
    /// The second gradient color for the stargazer badge.
    /// Only customizable by Stargazer Pro subscribers.
    /// </summary>
    public string StarColor2 { get; set; }

    /// <summary>
    /// Platform badge bits the user has chosen not to display. Zero means every
    /// earned platform badge is visible, including newly introduced badges.
    /// </summary>
    public long HiddenBadgeFlags { get; set; }

    public const string DefaultAvatar = "_content/Valour.Client/media/user-icons/icon-0.webp";
}

/// <summary>
/// Members every user model shares, whichever side it lives on.
/// </summary>
public static class SharedUserExtensions
{
    private static readonly FrozenDictionary<AvatarFormat, string> AvatarFormatMap = new Dictionary<AvatarFormat, string>()
    {
        { AvatarFormat.Webp64, "64.webp" },
        { AvatarFormat.Webp128, "128.webp" },
        { AvatarFormat.Webp256, "256.webp" },
        
        { AvatarFormat.Jpeg64, "64.jpg" },
        { AvatarFormat.Jpeg128, "128.jpg" },
        { AvatarFormat.Jpeg256, "256.jpg" },
        
        { AvatarFormat.WebpAnimated64, "anim-64.webp" },
        { AvatarFormat.WebpAnimated128, "anim-128.webp" },
        { AvatarFormat.WebpAnimated256, "anim-256.webp" },
        
        { AvatarFormat.Gif64, "anim-64.gif" },
        { AvatarFormat.Gif128, "anim-128.gif" },
        { AvatarFormat.Gif256, "anim-256.gif" },
    }.ToFrozenDictionary();
    
    private static readonly FrozenSet<AvatarFormat> AnimatedFormats = new HashSet<AvatarFormat>()
    {
        AvatarFormat.Gif64,
        AvatarFormat.Gif128,
        AvatarFormat.Gif256,
        AvatarFormat.WebpAnimated64,
        AvatarFormat.WebpAnimated128,
        AvatarFormat.WebpAnimated256,
    }.ToFrozenSet();
    
    private static readonly FrozenDictionary<AvatarFormat, AvatarFormat> AnimatedToStaticBackup = new Dictionary<AvatarFormat, AvatarFormat>()
    {
        { AvatarFormat.Gif64, AvatarFormat.Webp64 },
        { AvatarFormat.Gif128, AvatarFormat.Webp128 },
        { AvatarFormat.Gif256, AvatarFormat.Webp256 },
        { AvatarFormat.WebpAnimated64, AvatarFormat.Webp64 },
        { AvatarFormat.WebpAnimated128, AvatarFormat.Webp128 },
        { AvatarFormat.WebpAnimated256, AvatarFormat.Webp256 },
    }.ToFrozenDictionary();

    extension(ISharedUser user)
    {
        /// <summary>
        /// The subscription the user currently has.
        /// </summary>
        public UserSubscriptionType Subscription =>
            user.SubscriptionType is null ? null : UserSubscriptionTypes.TypeMap[user.SubscriptionType];

        /// <summary>
        /// How long ago the user was last active.
        /// </summary>
        public TimeSpan LastActiveSpan => DateTime.UtcNow.Subtract(user.TimeLastActive);

        /// <summary>
        /// The current activity state of the user. A state code of zero means
        /// the state follows from how recently the user was active.
        /// </summary>
        public UserState UserState
        {
            get
            {
                if (user.UserStateCode != 0)
                    return UserState.States[user.UserStateCode];

                var minutesPassed = DateTime.UtcNow.Subtract(user.TimeLastActive).TotalMinutes;
                return minutesPassed switch
                {
                    < 3 => UserState.Online,
                    < 6 => UserState.Away,
                    _ => UserState.Offline,
                };
            }
            set => user.UserStateCode = value.Value;
        }

        /// <summary>
        /// The user's name with their tag, such as "name#tag". Null for a null user.
        /// </summary>
        public string NameAndTag => user is null ? null : $"{user.Name}#{user.Tag}";

        /// <summary>
        /// One of the built-in avatars, picked from the user's ID, for users
        /// without a custom avatar or whose avatar failed to load.
        /// </summary>
        public string GetFailedAvatar() =>
            user is null ? ISharedUser.DefaultAvatar : $"_content/Valour.Client/media/user-icons/icon-{user.Id % 5}.webp";

        /// <summary>
        /// The avatar URL in the requested format. Animated formats fall back to
        /// the static image when the user has no animated avatar.
        /// </summary>
        public string GetAvatar(AvatarFormat format = AvatarFormat.Webp256)
        {
            if (user is null)
                return ISharedUser.DefaultAvatar;

            if (!user.HasCustomAvatar)
                return user.GetFailedAvatar();

            if (!user.HasAnimatedAvatar && AnimatedFormats.Contains(format))
                format = AnimatedToStaticBackup[format];

            return $"{ValourHosts.PublicCdnBaseUrl}/valour-public/avatars/{user.Id}/{AvatarFormatMap[format]}?v={user.Version}";
        }
    }
}
