namespace Valour.Server.Mapping;

public static class PlaneBanMapper
{
    public static PlanetBan ToModel(this Valour.Database.PlanetBan ban)
    {
        if (ban is null)
            return null;
        
        return new PlanetBan()
        {
            Id = ban.Id,
            PlanetId = ban.PlanetId,
            IssuerId = ban.IssuerId,
            TargetId = ban.TargetId,
            Reason = ban.Reason,
            TimeCreated = ban.TimeCreated,
            TimeExpires = ban.TimeExpires
        };
    }
    
    public static Valour.Database.PlanetBan ToDatabase(this PlanetBan ban)
    {
        if (ban is null)
            return null;
        
        return new Valour.Database.PlanetBan()
        {
            Id = ban.Id,
            PlanetId = ban.PlanetId,
            IssuerId = ban.IssuerId,
            TargetId = ban.TargetId,
            Reason = ban.Reason,
            TimeCreated = ban.TimeCreated,
            TimeExpires = ban.TimeExpires
        };
    }
}