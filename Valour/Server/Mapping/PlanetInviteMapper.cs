namespace Valour.Server.Mapping;

public static class PlanetInviteMapper
{
    public static PlanetInvite ToModel(this Valour.Database.PlanetInvite invite)
    {
        if (invite is null)
            return null;
        
        return new PlanetInvite()
        {
            Id = invite.Id,
            PlanetId = invite.PlanetId,
            Code = invite.Code,
            IssuerId = invite.IssuerId,
            TimeCreated = invite.TimeCreated,
            TimeExpires = invite.TimeExpires
        };
    }
    
    public static Valour.Database.PlanetInvite ToDatabase(this PlanetInvite invite)
    {
        if (invite is null)
            return null;
        
        return new Valour.Database.PlanetInvite()
        {
            Id = invite.Id,
            PlanetId = invite.PlanetId,
            Code = invite.Code,
            IssuerId = invite.IssuerId,
            TimeCreated = invite.TimeCreated,
            TimeExpires = invite.TimeExpires
        };
    }
}