using Valour.Sdk.Models;

namespace Valour.Sdk.Utility;

public static class PfpUtility
{
    public static string GetPfpUrl(User user, PlanetMember member = null)
    {
        if (member is null || string.IsNullOrWhiteSpace(member.MemberPfp))
        {
            if (user is null)
                return "_content/Valour.Client/media/user-icons/icon-0.png";
            
            if (string.IsNullOrWhiteSpace(user.PfpUrl))
                return user.GetFailedPfpUrl();

            return user.PfpUrl;
        }

        return member.MemberPfp;
    }

    public static string GetFailedPfpUrl(User user)
    {
        return user?.GetFailedPfpUrl() ?? "_content/Valour.Client/media/user-icons/icon-0.png";
    }
}