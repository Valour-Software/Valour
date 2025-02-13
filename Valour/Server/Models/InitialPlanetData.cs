using System.Collections.Immutable;

namespace Valour.Server.Models;

/// <summary>
/// All the data needed for the client to connect to a planet,
/// condensed into one object.
/// </summary>
public class InitialPlanetData
{
    /// <summary>
    /// Channels the user has access to
    /// </summary>
    public ImmutableList<Channel> Channels { get; set; }
    
    /// <summary>
    /// All the roles in the planet
    /// </summary>
    public ImmutableList<PlanetRole> Roles { get; set; }
}