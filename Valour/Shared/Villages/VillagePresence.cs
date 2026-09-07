namespace Valour.Shared.Villages;

/// <summary>
/// Which way a character is facing. Stored in presence so the renderer can
/// pick directional art later without a protocol change.
/// </summary>
public enum VillageFacing
{
    Down = 0,
    Left = 1,
    Right = 2,
    Up = 3,
}

/// <summary>
/// The live state of one member inside a village map.
///
/// Presence is ephemeral and never persisted: it is rebuilt from scratch when a
/// node restarts and is authoritative only for as long as the member is
/// connected. It deliberately carries no appearance data - the client already
/// has member avatars - so movement broadcasts stay small.
/// </summary>
public class VillagePresence
{
    public long PlanetId { get; set; }

    public long MapId { get; set; }

    public long UserId { get; set; }

    public long MemberId { get; set; }

    /// <summary>
    /// Display name and avatar are carried on join and in snapshots only, never
    /// on movement. Identity does not change while someone walks, so repeating
    /// it eight times a second would be pure overhead on the hot path.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>
    /// Tile coordinates. Movement is tile-quantized, so a move broadcast is a
    /// pair of small ints rather than a stream of floats; clients interpolate
    /// between tiles themselves.
    /// </summary>
    public int X { get; set; }

    public int Y { get; set; }

    public VillageFacing Facing { get; set; }

    /// <summary>
    /// The building the member is currently standing inside, if any. Drives
    /// which voice room they belong to.
    /// </summary>
    public long? BuildingId { get; set; }
}

/// <summary>
/// The full occupancy of a map, sent once when a member joins it.
/// </summary>
public class VillagePresenceSnapshot
{
    public long PlanetId { get; set; }
    public long MapId { get; set; }
    public List<VillagePresence> Presences { get; set; } = new();
}

/// <summary>
/// A single member's movement within a map. Kept separate from the full
/// presence model so the hot path stays as small as possible.
/// </summary>
public class VillagePresenceMove
{
    public long PlanetId { get; set; }
    public long MapId { get; set; }
    public long UserId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public VillageFacing Facing { get; set; }
    public long? BuildingId { get; set; }
}

/// <summary>
/// A member leaving a map, either by walking into another one or disconnecting.
/// </summary>
public class VillagePresenceLeft
{
    public long PlanetId { get; set; }
    public long MapId { get; set; }
    public long UserId { get; set; }
}
