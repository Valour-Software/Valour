namespace Valour.Server.Mapping;

public static class AutomodTriggerMapper
{
    public static AutomodTrigger ToModel(this Valour.Database.AutomodTrigger trigger)
    {
        if (trigger is null)
            return null;
        return new AutomodTrigger
        {
            Id = trigger.Id,
            PlanetId = trigger.PlanetId,
            MemberAddedBy = trigger.MemberAddedBy,
            Type = trigger.Type,
            Name = trigger.Name,
            TriggerWords = trigger.TriggerWords
        };
    }

    public static Valour.Database.AutomodTrigger ToDatabase(this AutomodTrigger trigger)
    {
        if (trigger is null)
            return null;
        return new Valour.Database.AutomodTrigger
        {
            Id = trigger.Id,
            PlanetId = trigger.PlanetId,
            MemberAddedBy = trigger.MemberAddedBy,
            Type = trigger.Type,
            Name = trigger.Name,
            TriggerWords = trigger.TriggerWords
        };
    }
}
