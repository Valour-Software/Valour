using Valour.Sdk.E2ee;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Changes who receives a planet's channel keys and whether new members can
/// read earlier messages. Every planet is end-to-end encrypted. An open planet
/// can become invite-only but not the reverse, because members' apps keep
/// enforcing a membership log once they have seen it.
/// </summary>
public class PlanetEncryptionService
{
    private readonly ValourDb _db;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly E2eeAccessLogService _accessLogs;
    private readonly CoreHubService _coreHub;

    public PlanetEncryptionService(ValourDb db, HostedPlanetService hostedPlanetService,
        E2eeAccessLogService accessLogs, CoreHubService coreHub)
    {
        _db = db;
        _hostedPlanetService = hostedPlanetService;
        _accessLogs = accessLogs;
        _coreHub = coreHub;
    }

    public async Task<TaskResult<Planet>> SetEncryptionAsync(long planetId, long userId, SetPlanetEncryptionRequest request)
    {
        if (request is null)
            return TaskResult<Planet>.FromFailure("Include the encryption settings.");

        var planet = await _db.Planets.FirstOrDefaultAsync(x => x.Id == planetId);
        if (planet is null)
            return TaskResult<Planet>.FromFailure("Planet not found.");
        if (planet.OwnerId != userId)
            return TaskResult<Planet>.FromFailure("Only the planet owner can change encryption.");
        if (planet.LockedForMigration)
            return TaskResult<Planet>.FromFailure(MigrationLock.Message);
        if (!Enum.IsDefined(request.Mode))
            return TaskResult<Planet>.FromFailure("Unknown encryption mode.");

        // Members' apps remember that the planet has a signed membership log
        // and keep enforcing it, so a server claim that the planet is open
        // again cannot hand keys to accounts no admin admitted.
        if (request.Mode == PlanetEncryptionMode.Open && planet.EncryptionMode == PlanetEncryptionMode.InviteOnly)
            return TaskResult<Planet>.FromFailure("An invite-only planet cannot become open again.");

        if (request.Mode == PlanetEncryptionMode.InviteOnly)
        {
            var hasLog = await _accessLogs.ExistsAsync(AccessLogScope.Planet, planetId);
            if (!hasLog && request.Genesis is null)
                return TaskResult<Planet>.FromFailure("Sign the membership log to make the planet invite-only.");

            if (request.Genesis is not null)
            {
                var result = await _accessLogs.AppendAsync(AccessLogScope.Planet, planetId, userId, request.Genesis,
                    allowStart: true);
                if (!result.Success)
                    return TaskResult<Planet>.FromFailure(result.Message);
            }
        }

        planet.EncryptionMode = request.Mode;
        planet.EncryptionSharesHistory = request.SharesHistory;
        await _db.SaveChangesAsync();

        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        hosted.Planet.EncryptionMode = request.Mode;
        hosted.Planet.EncryptionSharesHistory = request.SharesHistory;
        _coreHub.NotifyPlanetChange(hosted.Planet);

        return TaskResult<Planet>.FromData(hosted.Planet);
    }
}
