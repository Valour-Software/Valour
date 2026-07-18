namespace Valour.Server.Services;

/// <summary>
/// Shared constant for the planet read-only guard used while a migration is in
/// progress. A locked planet rejects mutations so nothing is lost between the
/// snapshot and the handoff.
/// </summary>
public static class MigrationLock
{
    public const string Message = "This planet is being migrated and is temporarily read-only.";
}
