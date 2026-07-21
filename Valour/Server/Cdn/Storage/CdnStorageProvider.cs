using Amazon.Runtime;
using Amazon.S3;
using Valour.Config.Configs;

namespace Valour.Server.Cdn.Storage;

public enum CdnStorageMode
{
    S3,
    FileSystem
}

/// <summary>
/// Resolves the configured storage backends for the CDN layer from CdnConfig.
/// Mode selection: explicit CdnConfig.StorageMode wins; otherwise "s3" when an
/// S3 endpoint is configured, else "filesystem" (the zero-config self-host
/// default — uploads work out of the box with no cloud account).
/// </summary>
public class CdnStorageProvider
{
    /// <summary>
    /// Storage for private content (message/file uploads). Served through the
    /// /content routes; objects are keyed by content hash.
    /// </summary>
    public IObjectStorage Private { get; }

    /// <summary>
    /// Storage for public assets (avatars, planet icons, emoji, themes).
    /// Served from the public CDN host under the /valour-public/ URL namespace.
    /// </summary>
    public IObjectStorage Public { get; }

    public CdnStorageMode Mode { get; }

    public CdnStorageProvider(IHostEnvironment env, ILogger<CdnStorageProvider> logger)
    {
        var config = CdnConfig.Current;
        Mode = ResolveMode(config);

        if (Mode == CdnStorageMode.S3)
        {
            var privateCredentials = new BasicAWSCredentials(config.S3Access, config.S3Secret);
            var privateClient = new AmazonS3Client(privateCredentials, new AmazonS3Config
            {
                ServiceURL = config.S3Endpoint
            });

            var publicCredentials = new BasicAWSCredentials(config.PublicS3Access, config.PublicS3Secret);
            var publicClient = new AmazonS3Client(publicCredentials, new AmazonS3Config
            {
                ServiceURL = config.PublicS3Endpoint
            });

            Private = new S3ObjectStorage(privateClient, config.PrivateBucket, logger);
            Public = new S3ObjectStorage(publicClient, config.PublicBucket, logger);

            logger.LogInformation(
                "CDN storage mode: s3 (private bucket {PrivateBucket}, public bucket {PublicBucket})",
                config.PrivateBucket, config.PublicBucket);
        }
        else
        {
            var root = config?.FileSystemPath;
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(env.ContentRootPath, "media-storage");

            Private = new FileSystemObjectStorage(Path.Combine(root, "private"), logger);
            Public = new FileSystemObjectStorage(Path.Combine(root, "public"), logger);

            logger.LogInformation("CDN storage mode: filesystem ({Root})", Path.GetFullPath(root));
        }
    }

    internal static CdnStorageMode ResolveMode(CdnConfig? config)
    {
        var explicitMode = config?.StorageMode;
        if (!string.IsNullOrWhiteSpace(explicitMode))
        {
            if (string.Equals(explicitMode, "s3", StringComparison.OrdinalIgnoreCase))
                return CdnStorageMode.S3;

            if (string.Equals(explicitMode, "filesystem", StringComparison.OrdinalIgnoreCase))
                return CdnStorageMode.FileSystem;

            throw new InvalidOperationException(
                $"Unknown CDN StorageMode '{explicitMode}'. Valid values: \"s3\", \"filesystem\".");
        }

        return string.IsNullOrWhiteSpace(config?.S3Endpoint)
            ? CdnStorageMode.FileSystem
            : CdnStorageMode.S3;
    }
}
