using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Planet : Item, ISharedPlanet
{
    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    public long OwnerId { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The node this planet belongs to
    /// </summary>
    public string NodeName { get; set; } 

    /// <summary>
    /// The image url for the planet 
    /// </summary>
    public string IconUrl { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    public bool Public { get; set; }

    /// <summary>
    /// If the server should show up on the discovery tab
    /// </summary>
    public bool Discoverable { get; set; }
}