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
}

/// <summary>
/// Used to generalize role-related events on members
/// </summary>
public readonly struct RoleMembershipEvent
{
    /// <summary>
    /// The type of event that occured
    /// </summary>
    public readonly MemberRoleEventType Type;

    /// <summary>
    /// The role the event relates to
    /// </summary>
    public readonly PlanetRole Role;

    /// <summary>
    /// The member the event relates to
    /// </summary>
    public readonly PlanetMember Member;
    
    public RoleMembershipEvent(MemberRoleEventType type, PlanetRole role, PlanetMember member)
    {
        Type = type;
        Role = role;
        Member = member;
    }
}