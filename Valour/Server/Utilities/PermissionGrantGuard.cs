#nullable enable

using Valour.Shared.Authorization;

namespace Valour.Server.Utilities;

/// <summary>
/// Helpers for ensuring members only grant or revoke permission bits they hold themselves.
/// </summary>
public static class PermissionGrantGuard
{
    /// <summary>
    /// Returns the bits whose effective state differs between two permission node states.
    /// A bit changes when it becomes set or unset, or when its allowed value changes while set.
    /// </summary>
    public static long GetChangedNodeBits(long oldCode, long oldMask, long newCode, long newMask) =>
        (oldMask ^ newMask) | ((oldCode & oldMask) ^ (newCode & newMask));

    /// <summary>
    /// Returns an error message naming the changed bits the member does not hold,
    /// or null when every changed bit is held.
    /// </summary>
    public static string? GetUnheldChangeError(long changedBits, long heldBits, Permission[] permissionSet, string scope)
    {
        var unheld = changedBits & ~heldBits;
        if (unheld == 0)
            return null;

        var names = permissionSet?
            .Where(p => p.Value != Permission.FULL_CONTROL && (p.Value & unheld) != 0)
            .Select(p => p.Name)
            .ToList();

        var described = names is { Count: > 0 } ? string.Join(", ", names) : "unknown permissions";
        return $"You cannot grant or revoke {scope} permissions you do not have: {described}.";
    }
}
