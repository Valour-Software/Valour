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
            RoleMembershipHash = member.RoleMembershipHash,
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
            RoleMembershipHash = member.RoleMembershipHash
        };
    }
}