namespace Valour.Shared.Models;

/// <summary>
/// Whether a village map is the open world or the inside of a building.
/// Interiors are first-class maps rather than a special case, so a building
/// can link to one without the renderer or persistence treating it differently.
/// </summary>
public enum VillageMapType
{
    Outdoor = 0,
    Interior = 1,
}

/// <summary>
/// How a building provides voice to the members standing inside it.
/// </summary>
public enum VillageVoiceMode
{
    /// <summary>
    /// The building has no voice.
    /// </summary>
    None = 0,

    /// <summary>
    /// The building is bound to an existing planet voice channel.
    /// </summary>
    LinkedChannel = 1,

    /// <summary>
    /// The building creates a temporary voice room on demand when someone
    /// enters, and releases it once the building empties.
    /// </summary>
    AutoRoom = 2,
}

/// <summary>
/// Who may edit the contents of a plot or the interior of a building.
/// </summary>
public enum VillageEditMode
{
    /// <summary>
    /// Only members holding the ManageVillage planet permission.
    /// </summary>
    Managers = 0,

    /// <summary>
    /// The owning member, plus managers.
    /// </summary>
    Owner = 1,

    /// <summary>
    /// Any member of the planet.
    /// </summary>
    Everyone = 2,
}
