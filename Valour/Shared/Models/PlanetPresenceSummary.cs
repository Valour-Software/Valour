namespace Valour.Shared.Models;

/// <summary>
/// Lightweight snapshot of who is currently active on a planet,
/// used for presence hints on thread cards and live-chat CTAs.
/// </summary>
public class PlanetPresenceSummary
{
    /// <summary>
    /// Number of members recently active on the planet
    /// </summary>
    public int ChattingCount { get; set; }

    /// <summary>
    /// A small sample of the most recently active members, for avatar stacks
    /// </summary>
    public List<PresenceAvatar> Avatars { get; set; } = new();
}

public class PresenceAvatar
{
    public string Name { get; set; }
    public string AvatarUrl { get; set; }
}
