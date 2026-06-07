namespace Valour.Server.Mapping;

public static class PlanetRuleMapper
{
    public static PlanetRule ToModel(this Valour.Database.PlanetRule rule)
    {
        if (rule is null)
            return null;

        return new PlanetRule
        {
            Id = rule.Id,
            PlanetId = rule.PlanetId,
            Position = rule.Position,
            Title = rule.Title,
            Description = rule.Description
        };
    }

    public static Valour.Database.PlanetRule ToDatabase(this PlanetRule rule)
    {
        if (rule is null)
            return null;

        return new Valour.Database.PlanetRule
        {
            Id = rule.Id,
            PlanetId = rule.PlanetId,
            Position = rule.Position,
            Title = rule.Title,
            Description = rule.Description
        };
    }
}
