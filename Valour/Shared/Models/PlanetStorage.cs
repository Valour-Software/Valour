namespace Valour.Shared.Models;

/// <summary>
/// Public (secret-free) view of a planet's bring-your-own-storage config.
/// </summary>
public class PlanetStorageInfo
{
    public long PlanetId { get; set; }
    public string Endpoint { get; set; }
    public string Bucket { get; set; }
    public string Region { get; set; }
    public string PublicBaseUrl { get; set; }
    public bool Enabled { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

/// <summary>
/// Write request for a planet's storage config. Keys are write-only: leave
/// null/empty on update to keep the stored (encrypted) credentials.
/// </summary>
public class PlanetStorageConfigRequest
{
    public string Endpoint { get; set; }
    public string Bucket { get; set; }
    public string Region { get; set; }
    public string AccessKey { get; set; }
    public string SecretKey { get; set; }
    public string PublicBaseUrl { get; set; }
    public bool Enabled { get; set; }
}

/// <summary>
/// A member's request to upload a file directly to the planet's own storage.
/// </summary>
public class PlanetMediaUploadRequest
{
    /// <summary>
    /// The channel the upload is destined for — the member must have
    /// AttachContent permission there.
    /// </summary>
    public long ChannelId { get; set; }

    public string FileName { get; set; }
    public string MimeType { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>
    /// Client-computed SHA-256 (hex) of the exact bytes to be uploaded.
    /// Used in the object key and recorded on the attachment for forensics.
    /// </summary>
    public string Sha256 { get; set; }
}

/// <summary>
/// A short-lived grant to PUT one object directly into the planet's bucket.
/// The upload must use the exact content type and byte length that were
/// requested — both are signed into the URL.
/// </summary>
public class PlanetMediaUploadGrant
{
    public string UploadUrl { get; set; }

    /// <summary>
    /// Where the object will be publicly served from once uploaded — used as
    /// the attachment location.
    /// </summary>
    public string PublicUrl { get; set; }

    public string Key { get; set; }
    public string ContentType { get; set; }
    public DateTime ExpiresAt { get; set; }
}
