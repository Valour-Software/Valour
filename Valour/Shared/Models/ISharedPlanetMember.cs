namespace Valour.Shared.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


/// <summary>
/// This represents a user within a planet and is used to represent membership
/// </summary>
public interface ISharedPlanetMember : ISharedPlanetModel<long>
{
    const string BaseRoute = "api/members";
    
    public ISharedUser GetSharedUser();
    
    /// <summary>
    /// The user within the planet
    /// </summary>
    long UserId { get; set; }

    /// <summary>
    /// The name to be used within the planet
    /// </summary>
    string Nickname { get; set; }

    /// <summary>
    /// The pfp to be used within the planet
    /// </summary>
    string MemberAvatar { get; set; }

    public PlanetRoleMembership RoleMembership { get; set; }

    /// <summary>
    /// The id of the most recent pinned thread this member dismissed ("marked as read").
    /// When this matches the planet's <see cref="ISharedPlanet.PinnedThreadId"/>, the pin
    /// no longer floats to the top of this member's feed.
    /// </summary>
    long? DismissedPinThreadId { get; set; }

    /// <summary>
    /// The last time this member connected to planet realtime.
    /// </summary>
    DateTime TimeLastConnected { get; set; }
    
    public static TaskResult ValidateName(ISharedPlanetMember member)
    {
        // Ensure nickname is valid
        return member.Nickname.Length > 32 ? new TaskResult(false, "Maximum nickname is 32 characters.") : 
            TaskResult.SuccessResult;
    }

    public string Name => GetName(this);
    
    public static string GetName(ISharedPlanetMember member) =>
        string.IsNullOrWhiteSpace(member.Nickname) ? 
            (member.GetSharedUser()?.Name ?? "User not found") : 
            member.Nickname;

    public string GetAvatar(AvatarFormat format = AvatarFormat.Webp256) =>
        GetAvatar(this, format);
    
    public static string GetAvatar(ISharedPlanetMember member, AvatarFormat format = AvatarFormat.Webp256)
    {
        if (!string.IsNullOrWhiteSpace(member.MemberAvatar))
        {
            // Native per-planet avatars are stored as the canonical 256px URL.
            // Select the generated size that the caller actually requested while
            // leaving legacy/external member-avatar URLs untouched.
            var avatar = member.MemberAvatar;
            var queryIndex = avatar.IndexOf('?');
            var path = queryIndex < 0 ? avatar : avatar[..queryIndex];
            var query = queryIndex < 0 ? string.Empty : avatar[queryIndex..];
            if (path.Contains("/memberavatars/", StringComparison.Ordinal) && path.EndsWith("/256.webp", StringComparison.Ordinal))
            {
                var size = format switch
                {
                    AvatarFormat.Jpeg64 or AvatarFormat.Gif64 or AvatarFormat.Webp64 or AvatarFormat.WebpAnimated64 => 64,
                    AvatarFormat.Jpeg128 or AvatarFormat.Gif128 or AvatarFormat.Webp128 or AvatarFormat.WebpAnimated128 => 128,
                    _ => 256
                };
                return $"{path[..(path.Length - "256.webp".Length)]}{size}.webp{query}";
            }

            return avatar;
        }

        return member.GetSharedUser()?.GetAvatar(format) ?? ISharedUser.DefaultAvatar;
    }
}

