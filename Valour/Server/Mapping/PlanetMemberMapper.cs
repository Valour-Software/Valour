using Valour.Shared.Models;

namespace Valour.Server.Mapping;

public static class PlanetMemberMapper
{
    /// <summary>
    /// Produces a fresh member instance copied from a cached core, attaching the provided user.
    /// Hand-written (not reflection-based CopyAllTo) since this runs on hot member-read paths.
    /// </summary>
    public static PlanetMember CopyWithUser(this PlanetMember core, User user)
    {
        if (core is null)
            return null;

        return new PlanetMember()
        {
            Id = core.Id,
            UserId = core.UserId,
            PlanetId = core.PlanetId,
            Nickname = core.Nickname,
            MemberAvatar = core.MemberAvatar,
            RoleMembership = core.RoleMembership,
            DismissedPinThreadId = core.DismissedPinThreadId,
            TimeLastConnected = core.TimeLastConnected,
            User = user
        };
    }

    public static PlanetMember ToModel(this Valour.Database.PlanetMember member)
    {
        if (member is null)
            return null;
        
        return new PlanetMember()
        {
            Id = member.Id,
            UserId = member.UserId,
            PlanetId = member.PlanetId,
            Nickname = member.Nickname,
            MemberAvatar = member.MemberAvatar,
            RoleMembership = member.RoleMembership,
            DismissedPinThreadId = member.DismissedPinThreadId,
            TimeLastConnected = member.TimeLastConnected,
            User = member.User?.ToModel()
        };
    }
    
    public static Valour.Database.PlanetMember ToDatabase(this PlanetMember member)
    {
        if (member is null)
            return null;
        
        return new Valour.Database.PlanetMember()
        {
            Id = member.Id,
            UserId = member.UserId,
            PlanetId = member.PlanetId,
            Nickname = member.Nickname,
            MemberAvatar = member.MemberAvatar,
            RoleMembership = member.RoleMembership,
            DismissedPinThreadId = member.DismissedPinThreadId,
            TimeLastConnected = member.TimeLastConnected
        };
    }
}