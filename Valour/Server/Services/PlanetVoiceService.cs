using Microsoft.AspNetCore.DataProtection;
using Valour.Config.Configs;
using Valour.Server.Cdn;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Bring-your-own-voice for planets. Valour holds the owner's (encrypted)
/// LiveKit API secret only to sign short-lived join tokens — call media flows
/// directly between members and the owner's SFU. Valour never carries,
/// records, or relays the streams. Mirrors <see cref="PlanetStorageService"/>.
/// </summary>
public class PlanetVoiceService
{
    public const string ProtectorPurpose = "Valour.PlanetVoice.Credentials";

    private readonly ValourDb _db;
    private readonly IDataProtector _protector;
    private readonly LiveKitService _liveKit;
    private readonly VoiceCoordinator _coordinator;
    private readonly ILogger<PlanetVoiceService> _logger;

    public PlanetVoiceService(
        ValourDb db,
        IDataProtectionProvider dataProtection,
        LiveKitService liveKit,
        VoiceCoordinator coordinator,
        ILogger<PlanetVoiceService> logger)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _liveKit = liveKit;
        _coordinator = coordinator;
        _logger = logger;
    }

    public async Task<PlanetVoiceInfo> GetInfoAsync(long planetId)
    {
        var config = await _db.PlanetVoiceConfigs.FindAsync(planetId);
        return config is null ? null : ToInfo(config);
    }

    public async Task<TaskResult<PlanetVoiceInfo>> SetConfigAsync(long planetId, PlanetVoiceConfigRequest request)
    {
        if (request is null)
            return TaskResult<PlanetVoiceInfo>.FromFailure("Include config in body.");

        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetVoiceInfo>.FromFailure(migrationGuard.Message);

        var urlCheck = await ValidateLiveKitUrlAsync(request.LiveKitUrl);
        if (!urlCheck.Success)
            return TaskResult<PlanetVoiceInfo>.FromFailure(urlCheck.Message);

        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return TaskResult<PlanetVoiceInfo>.FromFailure("API key is required.");

        var config = await _db.PlanetVoiceConfigs.FindAsync(planetId);
        var isNew = config is null;

        if (isNew && string.IsNullOrWhiteSpace(request.ApiSecret))
            return TaskResult<PlanetVoiceInfo>.FromFailure("API secret is required.");

        if (isNew)
        {
            config = new Valour.Database.PlanetVoiceConfig
            {
                PlanetId = planetId,
                CreatedAt = DateTime.UtcNow,
            };
            await _db.PlanetVoiceConfigs.AddAsync(config);
        }

        config.LiveKitUrl = request.LiveKitUrl.TrimEnd('/');
        config.ApiKey = request.ApiKey.Trim();
        config.Enabled = request.Enabled;
        config.UpdatedAt = DateTime.UtcNow;

        // The secret is write-only: empty means keep existing
        if (!string.IsNullOrWhiteSpace(request.ApiSecret))
            config.ApiSecretEncrypted = _protector.Protect(request.ApiSecret);

        // Config changed — require a fresh probe before trusting it
        config.VerifiedAt = null;

        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is not null)
            planet.SelfHostedVoice = request.Enabled;

        await _db.SaveChangesAsync();

        // Live calls must not keep using stale credentials
        _coordinator.InvalidatePlanet(planetId);

        return TaskResult<PlanetVoiceInfo>.FromData(ToInfo(config));
    }

    public async Task<TaskResult> ClearAsync(long planetId)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return migrationGuard;

        var config = await _db.PlanetVoiceConfigs.FindAsync(planetId);
        if (config is not null)
            _db.PlanetVoiceConfigs.Remove(config);

        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is not null)
            planet.SelfHostedVoice = false;

        await _db.SaveChangesAsync();

        _coordinator.InvalidatePlanet(planetId);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Signs an admin token and lists rooms on the owner's SFU — proves the URL
    /// is reachable and the key/secret pair is accepted.
    /// </summary>
    public async Task<TaskResult> ProbeAsync(long planetId)
    {
        var config = await _db.PlanetVoiceConfigs.FindAsync(planetId);
        if (config is null)
            return TaskResult.FromFailure("No voice config for this planet.");

        var creds = ToCredentials(config, _protector);

        var result = await _liveKit.ProbeWithCredentialsAsync(creds);
        if (!result.Success)
        {
            _logger.LogInformation("Voice probe failed for planet {PlanetId}: {Message}", planetId, result.Message);
            return result;
        }

        config.VerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return result;
    }

    /// <summary>
    /// Decrypts a stored config into usable LiveKit credentials. External=true —
    /// all Twirp calls to owner-supplied URLs go through the SSRF-safe path.
    /// </summary>
    public static LiveKitCredentials ToCredentials(
        Valour.Database.PlanetVoiceConfig config, IDataProtector protector) =>
        new(config.LiveKitUrl, null, config.ApiKey, protector.Unprotect(config.ApiSecretEncrypted), External: true);

    /// <summary>
    /// wss:// (TLS) with a public DNS name, matching the SSRF rules used for
    /// planet storage and federation. Self-hosted instances can opt in to
    /// private-network/plain-ws SFUs via Voice__AllowInsecurePlanetVoice.
    /// </summary>
    private static async Task<TaskResult> ValidateLiveKitUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return TaskResult.FromFailure("LiveKit URL is required.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return TaskResult.FromFailure("LiveKit URL must be an absolute URL.");

        if (VoiceConfig.Current?.AllowInsecurePlanetVoice == true)
            return TaskResult.SuccessResult;

        if (!uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase))
            return TaskResult.FromFailure("LiveKit URL must use wss:// (TLS).");

        // Validate the https twin of the websocket URL: same host/port, and the
        // admin API calls will actually go there.
        var httpsTwin = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps }.Uri;
        if (!await OutboundUrlSafetyValidator.IsSafeAsync(httpsTwin))
            return TaskResult.FromFailure("LiveKit URL must resolve to a public address.");

        return TaskResult.SuccessResult;
    }

    private static PlanetVoiceInfo ToInfo(Valour.Database.PlanetVoiceConfig config) => new()
    {
        PlanetId = config.PlanetId,
        LiveKitUrl = config.LiveKitUrl,
        ApiKey = config.ApiKey,
        Enabled = config.Enabled,
        VerifiedAt = config.VerifiedAt,
    };
}
