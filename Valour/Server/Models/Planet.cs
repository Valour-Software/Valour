using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Planet : Item, ISharedPlanet
{
    ///////////////
    // Constants //
    ///////////////

    /// <summary>
    /// The maximum planets a user is allowed to have. This will increase after 
    /// the alpha period is complete.
    /// </summary>
    [JsonIgnore] 
    public const int MaxOwnedPlanets = 5;

    /// <summary>
    /// The maximum planets a user is allowed to join. This will increase after the 
    /// alpha period is complete.
    /// </summary>
    [JsonIgnore] 
    public const int MaxJoinedPlanets = 20;

    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    public long OwnerId { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    public string Name { get; set; }

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

    /// <summary>
    /// The default role for the planet
    /// </summary>
    public long DefaultRoleId { get; set; }

    /// <summary>
    /// The id of the main channel of the planet
    /// </summary>
    public long PrimaryChannelId { get; set; }
}