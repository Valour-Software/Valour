using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Sdk.Utility;

public static class AvatarUtility
{
    public static string GetAvatarUrl(User user, PlanetMember member = null, AvatarFormat format = AvatarFormat.Webp256)
    {
        if (member is null || string.IsNullOrWhiteSpace(member.MemberAvatar))
        {
            return ISharedUser.GetAvatarUrl(user, format); 
        }

        return member.MemberAvatar;
    }

    public static string GetFailedPfpUrl(User user)
    {
        return ISharedUser.GetFailedAvatarUrl(user);
    }
}