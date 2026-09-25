using Valour.Sdk.E2ee;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Makes planets public or private and sets whether new members can read
/// earlier messages. Every planet is end-to-end encrypted. A public planet is
/// open: its permissions decide who receives keys. A private planet is
/// invite-only: its signed membership log decides, and the server cannot
/// sign that log. Each change of privacy therefore comes with an entry the
/// owner's device signed, appended in the same transaction that changes the
/// planet: a genesis or restart entry to make it private, and an open entry
/// to make it public again, which members' apps verify before they stop
/// enforcing the log.
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

    /// <summary>
    /// Whether a planet with this encryption mode may be public. A planet
    /// governed by a membership log only becomes public through an open entry
    /// its owner signs, so a stored or imported planet never combines the two.
    /// </summary>
    public static bool AllowsPublic(PlanetEncryptionMode mode) => mode != PlanetEncryptionMode.InviteOnly;

    public async Task<TaskResult<Planet>> SetPrivacyAsync(long planetId, long userId, SetPlanetPrivacyRequest request)
    {
        if (request is null)
            return TaskResult<Planet>.FromFailure("Include the privacy settings.");

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var planet = await _db.Planets.FirstOrDefaultAsync(x => x.Id == planetId);
        if (planet is null)
            return TaskResult<Planet>.FromFailure("Planet not found.");
        if (planet.OwnerId != userId)
            return TaskResult<Planet>.FromFailure("Only the planet owner can change whether the planet is public.");
        if (planet.LockedForMigration)
            return TaskResult<Planet>.FromFailure(MigrationLock.Message);

        var log = await _accessLogs.GetStateAsync(AccessLogScope.Planet, planetId);
        if (log is null && await _accessLogs.ExistsAsync(AccessLogScope.Planet, planetId))
            return TaskResult<Planet>.FromFailure("The planet's membership log could not be verified.");

        var governed = log?.IsGoverning == true;
        var makePrivate = !request.Public;
        if (makePrivate != governed)
        {
            // Members' apps follow the signed log, not this server. Without
            // the owner's entry the planet would say one thing while their
            // apps enforced another.
            if (request.Entry is null)
                return TaskResult<Planet>.FromFailure(makePrivate
                    ? "Sign the membership log from the owner's verified device to make the planet private."
                    : "Sign the change from the owner's verified device to make the planet public.");

            AccessLogRecord record;
            try
            {
                record = AccessLogRecord.Decode(request.Entry.Body ?? []);
            }
            catch (E2eeFormatException e)
            {
                return TaskResult<Planet>.FromFailure(e.Message);
            }

            var expected = !makePrivate ? AccessLogEntryType.Open
                : log is null ? AccessLogEntryType.Genesis
                : AccessLogEntryType.Restart;
            if (record.Type != expected)
                return TaskResult<Planet>.FromFailure("The signed membership log change does not match this change.");

            // Only the log's owner, with the keys it names, can make a planet
            // it opened private again; the log's verifier also checks the keys.
            if (makePrivate && log is not null && log.Owner.UserId != userId)
                return TaskResult<Planet>.FromFailure(
                    "Only the owner named in the planet's membership log can make it private again.");

            var appended = await _accessLogs.AppendAsync(AccessLogScope.Planet, planetId, userId, request.Entry,
                allowStart: true);
            if (!appended.Success)
                return TaskResult<Planet>.FromFailure(appended.Message);
        }
        else if (request.Entry is not null)
        {
            return TaskResult<Planet>.FromFailure("This change does not need a membership log entry.");
        }

        planet.Public = request.Public;
        planet.EncryptionMode = request.Public ? PlanetEncryptionMode.Open : PlanetEncryptionMode.InviteOnly;
        planet.EncryptionSharesHistory = request.SharesHistory;

        // A vanity invite would let anyone join, which a private planet does not allow.
        if (makePrivate)
            planet.VanityInviteEnabled = false;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        hosted.Planet.Public = planet.Public;
        hosted.Planet.EncryptionMode = planet.EncryptionMode;
        hosted.Planet.EncryptionSharesHistory = planet.EncryptionSharesHistory;
        hosted.Planet.VanityInviteEnabled = planet.VanityInviteEnabled;
        _coreHub.NotifyPlanetChange(hosted.Planet);

        return TaskResult<Planet>.FromData(hosted.Planet);
    }
}
