using Valour.Shared.Models;

namespace Valour.Server.Mapping;

public static class PlanetMemberMapper
{
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
            RoleMembership = new PlanetRoleMembership(member.Rf0, member.Rf1, member.Rf2, member.Rf3),
            User = member.User.ToModel()
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
            
            Rf0 = member.RoleMembership.Rf0,
            Rf1 = member.RoleMembership.Rf1,
            Rf2 = member.RoleMembership.Rf2,
            Rf3 = member.RoleMembership.Rf3,
        };
    }
}