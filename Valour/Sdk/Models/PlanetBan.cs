using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetBan : ClientPlanetModel<PlanetBan, long>, ISharedPlanetBan
{
    public override string BaseRoute => "api/bans";

    /// <summary>
    /// The user that banned the user
    /// </summary>
    public long IssuerId { get; set; }

    /// <summary>
    /// The planet this ban belongs to
    /// </summary>
    public long PlanetId { get; set; }
    protected override long? GetPlanetId() => PlanetId;
    
    /// <summary>
    /// The userId of the target that was banned
    /// </summary>
    public long TargetId { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time the ban expires. Null for permanent.
    /// </summary>
    public DateTime? TimeExpires { get; set; }

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    public bool Permanent => TimeExpires == null;

    public override PlanetBan AddToCache(bool skipEvents = true, )
    {
        return Client.Cache.PlanetBans.Put(Id, this);
    }

    public override PlanetBan RemoveFromCache()
    {
        return Client.Cache.PlanetBans.TakeAndRemove(Id);
    }
}
