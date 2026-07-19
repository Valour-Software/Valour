using Microsoft.EntityFrameworkCore;
using Valour.Shared;

namespace Valour.Server.Services;

/// <summary>
/// Shared guard for the planet read-only lock used while a migration is in
/// progress. A locked planet rejects mutations so nothing is lost between the
/// snapshot and the handoff.
/// </summary>
public static class MigrationLock
{
    public const string Message = "This planet is being migrated and is temporarily read-only.";

    /// <summary>
    /// Returns a failure result when the planet is locked for migration, else
    /// success. A cheap indexed primary-key lookup — used by write paths that
    /// don't already hold the planet's hosted-cache row.
    /// </summary>
    public static async Task<TaskResult> GuardAsync(ValourDb db, long? planetId)
    {
        if (planetId is null)
            return TaskResult.SuccessResult;

        var locked = await db.Planets.AsNoTracking()
            .Where(x => x.Id == planetId.Value)
            .Select(x => (bool?)x.LockedForMigration)
            .FirstOrDefaultAsync();

        return locked == true ? TaskResult.FromFailure(Message) : TaskResult.SuccessResult;
    }
}
