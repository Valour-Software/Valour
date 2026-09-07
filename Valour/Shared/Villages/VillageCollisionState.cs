namespace Valour.Shared.Villages;

/// <summary>
/// String-backed states authored per tileset cell. The serialized value is
/// deliberately not an enum so new states can round-trip through older tools
/// without being discarded. Unknown states block movement until the runtime
/// explicitly defines their behavior.
/// </summary>
public static class VillageCollisionState
{
    public const string Empty = "empty";
    public const string Solid = "solid";
    public const string Door = "door";

    public static string Normalize(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return Empty;

        return state.Trim().ToLowerInvariant() switch
        {
            "false" or "0" or "none" => Empty,
            "true" or "1" or "blocked" => Solid,
            var normalized => normalized,
        };
    }

    public static bool BlocksMovement(string? state) => Normalize(state) switch
    {
        Empty => false,
        Door => false,
        _ => true,
    };

    public static bool IsDoor(string? state) => Normalize(state) == Door;
}
