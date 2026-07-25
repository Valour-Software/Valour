namespace Valour.Shared.Models;

/// <summary>
/// A structure placed on a village map. Buildings are the bridge between the
/// world and the rest of Valour: they can open a chat channel, host a voice
/// meeting, and lead into their own interior map.
/// </summary>
public interface ISharedVillageBuilding : ISharedPlanetModel<long>
{
    public const string BaseRoute = "api/villagebuildings";
    public const int MaxNameLength = 48;
    public const int MaxDescriptionLength = 256;

    long MapId { get; set; }

    /// <summary>
    /// The interior this building leads into, if it can be entered
    /// </summary>
    long? InteriorMapId { get; set; }

    /// <summary>
    /// The plot this building stands on, if any
    /// </summary>
    long? PlotId { get; set; }

    /// <summary>
    /// A chat or voice channel surfaced by this building
    /// </summary>
    long? ChannelId { get; set; }

    string Name { get; set; }

    string Description { get; set; }

    int X { get; set; }

    int Y { get; set; }

    int Width { get; set; }

    int Height { get; set; }

    /// <summary>
    /// Tile the member must step onto to enter. Always walkable, even when it
    /// falls inside the building's own footprint.
    /// </summary>
    int DoorX { get; set; }

    int DoorY { get; set; }

    /// <summary>
    /// Logical sprite key into the map's tileset
    /// </summary>
    string SpriteKey { get; set; }

    long? OwnerMemberId { get; set; }

    VillageVoiceMode VoiceMode { get; set; }

    bool ForSale { get; set; }

    decimal Price { get; set; }

    /// <summary>
    /// Returns true when the tile falls inside the building's footprint.
    /// </summary>
    public static bool Contains(ISharedVillageBuilding building, int x, int y) =>
        x >= building.X && x < building.X + building.Width &&
        y >= building.Y && y < building.Y + building.Height;
}
