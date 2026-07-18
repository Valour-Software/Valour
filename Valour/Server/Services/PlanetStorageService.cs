using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.DataProtection;
using Valour.Server.Cdn;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Bring-your-own-storage for planets. Valour holds the owner's (encrypted)
/// S3 credentials only to mint short-lived presigned PUT grants — bytes flow
/// directly between members and the owner's bucket, and are served from the
/// owner's public base URL. Valour never receives, stores, scans, or serves
/// planet-hosted media.
/// </summary>
public class PlanetStorageService
{
    private const string ProtectorPurpose = "Valour.PlanetStorage.Credentials";
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(10);
    private static readonly Regex Sha256Regex = new("^[a-f0-9]{64}$", RegexOptions.Compiled);

    private readonly ValourDb _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<PlanetStorageService> _logger;

    public PlanetStorageService(ValourDb db, IDataProtectionProvider dataProtection, ILogger<PlanetStorageService> logger)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public async Task<PlanetStorageInfo> GetInfoAsync(long planetId)
    {
        var config = await _db.PlanetStorageConfigs.FindAsync(planetId);
        return config is null ? null : ToInfo(config);
    }

    public async Task<TaskResult<PlanetStorageInfo>> SetConfigAsync(long planetId, PlanetStorageConfigRequest request)
    {
        if (request is null)
            return TaskResult<PlanetStorageInfo>.FromFailure("Include config in body.");

        var endpointCheck = await ValidateExternalUrlAsync(request.Endpoint, "Endpoint");
        if (!endpointCheck.Success)
            return TaskResult<PlanetStorageInfo>.FromFailure(endpointCheck.Message);

        var publicCheck = await ValidateExternalUrlAsync(request.PublicBaseUrl, "Public base URL");
        if (!publicCheck.Success)
            return TaskResult<PlanetStorageInfo>.FromFailure(publicCheck.Message);

        if (string.IsNullOrWhiteSpace(request.Bucket))
            return TaskResult<PlanetStorageInfo>.FromFailure("Bucket is required.");

        var config = await _db.PlanetStorageConfigs.FindAsync(planetId);
        var isNew = config is null;

        if (isNew && (string.IsNullOrWhiteSpace(request.AccessKey) || string.IsNullOrWhiteSpace(request.SecretKey)))
            return TaskResult<PlanetStorageInfo>.FromFailure("Access key and secret key are required.");

        if (isNew)
        {
            config = new Valour.Database.PlanetStorageConfig
            {
                PlanetId = planetId,
                CreatedAt = DateTime.UtcNow,
            };
            await _db.PlanetStorageConfigs.AddAsync(config);
        }

        config.Endpoint = request.Endpoint.TrimEnd('/');
        config.Bucket = request.Bucket.Trim();
        config.Region = string.IsNullOrWhiteSpace(request.Region) ? null : request.Region.Trim();
        config.PublicBaseUrl = request.PublicBaseUrl.TrimEnd('/');
        config.Enabled = request.Enabled;
        config.UpdatedAt = DateTime.UtcNow;

        // Keys are write-only: empty means keep existing
        if (!string.IsNullOrWhiteSpace(request.AccessKey))
            config.AccessKeyEncrypted = _protector.Protect(request.AccessKey);
        if (!string.IsNullOrWhiteSpace(request.SecretKey))
            config.SecretKeyEncrypted = _protector.Protect(request.SecretKey);

        // Config changed — require a fresh probe before trusting it
        config.VerifiedAt = null;

        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is not null)
            planet.SelfHostedMedia = request.Enabled;

        await _db.SaveChangesAsync();

        return TaskResult<PlanetStorageInfo>.FromData(ToInfo(config));
    }

    public async Task<TaskResult> ClearAsync(long planetId)
    {
        var config = await _db.PlanetStorageConfigs.FindAsync(planetId);
        if (config is not null)
            _db.PlanetStorageConfigs.Remove(config);

        var planet = await _db.Planets.FindAsync(planetId);
        if (planet is not null)
            planet.SelfHostedMedia = false;

        await _db.SaveChangesAsync();
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Write/read/delete round-trip against the configured bucket.
    /// </summary>
    public async Task<TaskResult> ProbeAsync(long planetId)
    {
        var config = await _db.PlanetStorageConfigs.FindAsync(planetId);
        if (config is null)
            return TaskResult.FromFailure("No storage config for this planet.");

        var client = CreateClient(config);
        var key = $"valour-probe/{Guid.NewGuid():N}";
        var payload = "valour-storage-probe";

        try
        {
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = config.Bucket,
                Key = key,
                ContentBody = payload,
                // R2 wants unsigned payloads; the SDK forbids them over plain
                // HTTP (insecure/LAN mode), where signing works fine anyway.
                DisablePayloadSigning = config.Endpoint.StartsWith("https", StringComparison.OrdinalIgnoreCase),
            });

            using var response = await client.GetObjectAsync(config.Bucket, key);
            using var reader = new StreamReader(response.ResponseStream);
            var readBack = await reader.ReadToEndAsync();

            await client.DeleteObjectAsync(config.Bucket, key);

            if (readBack != payload)
                return TaskResult.FromFailure("Probe object read back with unexpected content.");

            config.VerifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return new TaskResult(true, "Write, read, and delete all succeeded.");
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "Storage probe failed for planet {PlanetId}", planetId);
            return TaskResult.FromFailure($"Probe failed: {e.Message}");
        }
    }

    /// <summary>
    /// Mints a short-lived presigned PUT for one object in the planet's bucket.
    /// Caller must already have verified channel attach permission.
    /// </summary>
    public async Task<TaskResult<PlanetMediaUploadGrant>> CreateUploadGrantAsync(
        long planetId, long userId, PlanetMediaUploadRequest request, long maxSizeBytes)
    {
        if (request is null)
            return TaskResult<PlanetMediaUploadGrant>.FromFailure("Include request in body.");

        var config = await _db.PlanetStorageConfigs.FindAsync(planetId);
        if (config is null || !config.Enabled)
            return TaskResult<PlanetMediaUploadGrant>.FromFailure("This planet does not use its own storage.");

        if (request.SizeBytes <= 0 || request.SizeBytes > maxSizeBytes)
            return TaskResult<PlanetMediaUploadGrant>.FromFailure($"File size must be between 1 and {maxSizeBytes} bytes.");

        if (string.IsNullOrWhiteSpace(request.Sha256) || !Sha256Regex.IsMatch(request.Sha256))
            return TaskResult<PlanetMediaUploadGrant>.FromFailure("A lowercase hex SHA-256 of the file is required.");

        if (string.IsNullOrWhiteSpace(request.MimeType) || request.MimeType.Length > 127)
            return TaskResult<PlanetMediaUploadGrant>.FromFailure("A valid mime type is required.");

        var extension = SanitizeExtension(Path.GetExtension(request.FileName ?? ""));
        var key = $"valour-media/{planetId}/{userId}/{request.Sha256}{extension}";

        var client = CreateClient(config);

        var presign = new GetPreSignedUrlRequest
        {
            BucketName = config.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(GrantLifetime),
            ContentType = request.MimeType,
        };

        string uploadUrl;
        try
        {
            uploadUrl = await client.GetPreSignedURLAsync(presign);

            // The SDK presigner always emits https; SigV4 does not sign the
            // scheme, so align it with the configured endpoint (plain-HTTP
            // endpoints only exist in insecure/LAN mode).
            if (config.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                uploadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                uploadUrl = "http://" + uploadUrl["https://".Length..];
            }
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "Failed to presign upload for planet {PlanetId}", planetId);
            return TaskResult<PlanetMediaUploadGrant>.FromFailure("Failed to create upload grant.");
        }

        return TaskResult<PlanetMediaUploadGrant>.FromData(new PlanetMediaUploadGrant
        {
            UploadUrl = uploadUrl,
            PublicUrl = $"{config.PublicBaseUrl}/{key}",
            Key = key,
            ContentType = request.MimeType,
            ExpiresAt = DateTime.UtcNow.Add(GrantLifetime),
        });
    }

    /// <summary>
    /// Returns the enabled public media base URL for a planet, or null.
    /// Used by message-send validation to allow planet-hosted attachments.
    /// </summary>
    public async Task<string> GetEnabledPublicBaseUrlAsync(long planetId)
    {
        var config = await _db.PlanetStorageConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.Enabled);

        return config?.PublicBaseUrl;
    }

    private AmazonS3Client CreateClient(Valour.Database.PlanetStorageConfig config)
    {
        var credentials = new BasicAWSCredentials(
            _protector.Unprotect(config.AccessKeyEncrypted),
            _protector.Unprotect(config.SecretKeyEncrypted));

        var s3Config = new AmazonS3Config
        {
            ServiceURL = config.Endpoint,
            ForcePathStyle = true,
        };

        if (!string.IsNullOrWhiteSpace(config.Region))
            s3Config.AuthenticationRegion = config.Region;

        return new AmazonS3Client(credentials, s3Config);
    }

    private static async Task<TaskResult> ValidateExternalUrlAsync(string url, string label)
    {
        if (string.IsNullOrWhiteSpace(url))
            return TaskResult.FromFailure($"{label} is required.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return TaskResult.FromFailure($"{label} must be an absolute URL.");

        // Self-hosted instances can opt in to private-network storage
        // (homelab MinIO etc.) — this disables the SSRF protections below.
        if (Valour.Config.Configs.CdnConfig.Current?.AllowInsecurePlanetStorage == true)
            return TaskResult.SuccessResult;

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return TaskResult.FromFailure($"{label} must use HTTPS.");

        // Same SSRF rules as all outbound fetches: public IPs only,
        // DNS-rebinding safe, no localhost/reserved ranges.
        if (!await OutboundUrlSafetyValidator.IsSafeAsync(uri))
            return TaskResult.FromFailure($"{label} must resolve to a public address.");

        return TaskResult.SuccessResult;
    }

    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "";

        extension = extension.ToLowerInvariant();
        return Regex.IsMatch(extension, @"^\.[a-z0-9]{1,10}$") ? extension : "";
    }

    private static PlanetStorageInfo ToInfo(Valour.Database.PlanetStorageConfig config) => new()
    {
        PlanetId = config.PlanetId,
        Endpoint = config.Endpoint,
        Bucket = config.Bucket,
        Region = config.Region,
        PublicBaseUrl = config.PublicBaseUrl,
        Enabled = config.Enabled,
        VerifiedAt = config.VerifiedAt,
    };
}
