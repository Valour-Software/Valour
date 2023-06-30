using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Models;

public interface ISharedUser : ISharedItem
{
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
    /// The url for the user's profile picture
    /// </summary>
    string PfpUrl { get; set; }

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
    /// The span of time from which the user was last active
    /// </summary>
    TimeSpan LastActiveSpan =>
        GetLastActiveSpan(this);

    /// <summary>
    /// The current activity state of the user
    /// </summary>
    [NotMapped]
    UserState UserState
    {
        get => GetUserState(this);
        set => SetUserState(this, value);
    }

    public static TimeSpan GetLastActiveSpan(ISharedUser user)
    {
        return DateTime.UtcNow.Subtract(user.TimeLastActive);
    }

    public static UserState GetUserState(ISharedUser user)
    {
        // Automatically determine
        if (user.UserStateCode == 0)
        {
            double minPassed = DateTime.UtcNow.Subtract(user.TimeLastActive).TotalMinutes;

            if (minPassed < 3)
            {
                return UserState.Online;
            }
            else if (minPassed < 6)
            {
                return UserState.Away;
            }
            else
            {
                return UserState.Offline;
            }
        }

        // User selected
        return UserState.States[user.UserStateCode];
    }

    public static void SetUserState(ISharedUser user, UserState state)
    {
        user.UserStateCode = state.Value;
    }
}