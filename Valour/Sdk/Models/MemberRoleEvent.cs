namespace Valour.Sdk.Models;

/// <summary>
/// Used to show what kind of role event occured
/// </summary>
public enum MemberRoleEventType
{
    /// <summary>
    /// The role as added to the member
    /// </summary>
    Added,
    
    /// <summary>
    /// The role was removed from the member
    /// </summary>
    Removed,
    
    /// <summary>
    /// The role was updated (and the member had it)
    /// </summary>
    Updated,
}

/// <summary>
/// Used to generalize role-related events on members
/// </summary>
public struct MemberRoleEvent
{
    /// <summary>
    /// The type of event that occured
    /// </summary>
    public MemberRoleEventType Type { get; set; }
    
    /// <summary>
    /// The role the event relates to
    /// </summary>
    public PlanetRole Role { get; set; }
}