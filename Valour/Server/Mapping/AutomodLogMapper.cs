using Valour.Server.Models;

namespace Valour.Server.Mapping;

public static class AutomodLogMapper
{
    public static AutomodLog ToModel(this Valour.Database.AutomodLog log)
    {
        if (log is null)
            return null;
        return new AutomodLog
        {
            Id = log.Id,
            PlanetId = log.PlanetId,
            TriggerId = log.TriggerId,
            MemberId = log.MemberId,
            MessageId = log.MessageId,
            TimeTriggered = log.TimeTriggered
        };
    }

    public static Valour.Database.AutomodLog ToDatabase(this AutomodLog log)
    {
        if (log is null)
            return null;
        return new Valour.Database.AutomodLog
        {
            Id = log.Id,
            PlanetId = log.PlanetId,
            TriggerId = log.TriggerId,
            MemberId = log.MemberId,
            MessageId = log.MessageId,
            TimeTriggered = log.TimeTriggered
        };
    }
}

