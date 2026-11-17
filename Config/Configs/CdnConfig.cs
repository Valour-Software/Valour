namespace Valour.Config.Configs;

public class CdnConfig
{
    /// <summary>
    /// The static instance of the current instance
    /// </summary>
    public static CdnConfig? Current;

    public CdnConfig()
    {
        Current = this;
    }

    // Cross-server authorization
    public string? Key { get; set; }

    /// <summary>
    /// Storage backend: "s3" or "filesystem". When unset, "s3" is used if
    /// S3Endpoint is configured, otherwise "filesystem" (local disk).
    /// </summary>
    public string? StorageMode { get; set; }

    /// <summary>
    /// Root directory for the "filesystem" storage mode. Defaults to
    /// "media-storage" under the server's content root.
    /// </summary>
    public string? FileSystemPath { get; set; }

    // S3 properties
    public string? S3Access { get; set; }
    public string? S3Secret { get; set; }
    public string? S3Endpoint { get; set; }

    // Public S3 properties
    public string? PublicS3Access { get; set; }
    public string? PublicS3Secret { get; set; }
    public string? PublicS3Endpoint { get; set; }

    /// <summary>
    /// Allows planet bring-your-own-storage endpoints on plain HTTP and
    /// private/LAN addresses. Intended for self-hosted instances whose
    /// communities run storage on the same network (e.g. a homelab MinIO).
    /// Leave false on public deployments — it disables SSRF protections
    /// for planet storage endpoints.
    /// </summary>
    public bool AllowInsecurePlanetStorage { get; set; }

    /// <summary>
    /// How many bytes of uploaded attachments each account on this server may
    /// keep. When unset, the Stargazer plan allowances apply. A positive value
    /// replaces them for every account, and 0 removes the limit. Servers that
    /// run their own community node use this to set what their members get.
    /// </summary>
    public long? UserStorageQuotaBytes { get; set; }

    /// <summary>
    /// The largest single file an account may upload to this server. When
    /// unset, the Stargazer plan limits apply. A positive value replaces them
    /// for every account.
    /// </summary>
    public long? MaxUploadBytes { get; set; }

    /// <summary>
    /// Bucket for private content (message/file uploads), s3 mode only.
    /// </summary>
    public string PrivateBucket { get; set; } = "valourmps";

    /// <summary>
    /// Bucket for public assets (avatars, icons, emoji, themes), s3 mode only.
    /// Note: the public URL namespace is always "/valour-public/" regardless of
    /// bucket name — the public CDN host mapping must account for this.
    /// </summary>
    public string PublicBucket { get; set; } = "valour-public";
}

