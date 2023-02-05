namespace Valour.Server.Mapping;

public static class PlanetRoleMemberMapper
{
    public static PlanetRoleMember ToModel(this Valour.Database.PlanetRoleMember roleMember)
    {
        if (roleMember is null)
            return null;
        
        return new PlanetRoleMember()
        {
            Id = roleMember.Id,
            PlanetId = roleMember.PlanetId,
            RoleId = roleMember.RoleId,
            UserId = roleMember.UserId,
            MemberId = roleMember.MemberId
        };
    }
    
    public static Valour.Database.PlanetRoleMember ToDatabase(this PlanetRoleMember roleMember)
    {
        if (roleMember is null)
            return null;
        
        return new Valour.Database.PlanetRoleMember()
        {
            Id = roleMember.Id,
            PlanetId = roleMember.PlanetId,
            RoleId = roleMember.RoleId,
            UserId = roleMember.UserId,
            MemberId = roleMember.MemberId
        };
    }
}