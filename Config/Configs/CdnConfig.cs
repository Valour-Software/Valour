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
    public string Key { get; set; }

    /// <summary>
    /// Storage backend: "s3" or "filesystem". When unset, "s3" is used if
    /// S3Endpoint is configured, otherwise "filesystem" (local disk).
    /// </summary>
    public string StorageMode { get; set; }

    /// <summary>
    /// Root directory for the "filesystem" storage mode. Defaults to
    /// "media-storage" under the server's content root.
    /// </summary>
    public string FileSystemPath { get; set; }

    // S3 properties
    public string S3Access { get; set; }
    public string S3Secret { get; set; }
    public string S3Endpoint { get; set; }

    // Public S3 properties
    public string PublicS3Access { get; set; }
    public string PublicS3Secret { get; set; }
    public string PublicS3Endpoint { get; set; }

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


