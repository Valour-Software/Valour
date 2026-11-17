using Valour.Shared.Cdn;

namespace Valour.Shared.Models;

/// <summary>
/// How much Valour-hosted attachment storage an account has used.
/// </summary>
public class UserStorageInfo
{
    /// <summary>The code returned when an upload is refused because storage is full.</summary>
    public const string StorageFullCode = "storage_full";

    public long UsedBytes { get; set; }

    /// <summary>
    /// The storage allowance in bytes. It is <see cref="long.MaxValue"/> when
    /// this server does not limit storage, which <see cref="Unlimited"/> reports.
    /// </summary>
    public long QuotaBytes { get; set; }
    public long MaxUploadBytes { get; set; }

    /// <summary>True when this server puts no limit on how much an account stores.</summary>
    public bool Unlimited => QuotaBytes == long.MaxValue;

    /// <summary>
    /// True when more space can be bought here through a Stargazer plan. A server
    /// whose operator sets its own limits has no plans to offer.
    /// </summary>
    public bool PlansAvailable { get; set; }

    /// <summary>The name of the active Stargazer tier, or null on the free plan.</summary>
    public string SubscriptionType { get; set; }

    /// <summary>
    /// True when the account has a working Google Drive connection. Files then
    /// move to Drive and uploads are not refused for lack of space.
    /// </summary>
    public bool DriveConnected { get; set; }

    /// <summary>
    /// True when new uploads go to Drive. Only then are uploads not refused for
    /// lack of space. Someone with Drive connected can choose to keep new uploads
    /// on Valour instead.
    /// </summary>
    public bool DriveIsDefault { get; set; }

    /// <summary>True when this server can connect Google Drive at all.</summary>
    public bool DriveAvailable { get; set; }

    /// <summary>Set to <see cref="StorageFullCode"/> on a refused upload.</summary>
    public string Code { get; set; }

    /// <summary>The size of the upload that was refused, when there was one.</summary>
    public long RequestedBytes { get; set; }

    public long AvailableBytes => Unlimited ? long.MaxValue : Math.Max(0, QuotaBytes - UsedBytes);

    public double UsedFraction => Unlimited ? 0 : QuotaBytes <= 0 ? 1 : Math.Min(1, (double)UsedBytes / QuotaBytes);

    public bool IsFull => !Unlimited && UsedBytes >= QuotaBytes;

    /// <summary>Usage at which the storage meters turn amber.</summary>
    public const double WarningFraction = 0.8;

    /// <summary>Usage at which the storage meters turn red.</summary>
    public const double CriticalFraction = 0.95;

    /// <summary>
    /// Formats a size the way the storage screens show it, for example 1.5 GB.
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0)
            return $"{value:0} {units[unit]}";

        // Round down, so an account with a sliver of room left never reads as full.
        return $"{Math.Floor(value * 10) / 10:0.#} {units[unit]}";
    }
}

/// <summary>
/// Where an attachment cleanup looks for files.
/// </summary>
public enum AttachmentCleanupScope
{
    /// <summary>Every attachment.</summary>
    All = 0,

    /// <summary>Attachments used in the chosen planets, and optionally direct messages.</summary>
    Selected = 1,

    /// <summary>Attachments that are not used by any message or thread.</summary>
    Unattached = 2,
}

/// <summary>
/// Chooses which attachments a cleanup covers. Every condition must match.
/// </summary>
public class AttachmentCleanupCriteria
{
    /// <summary>Only files uploaded before this time.</summary>
    public DateTime? CreatedBefore { get; set; }

    /// <summary>
    /// Files uploaded after this time are never included. The client sets it
    /// when the cleanup is opened, so a file uploaded while the screen is open
    /// cannot be swept in.
    /// </summary>
    public DateTime AsOf { get; set; }

    public AttachmentCleanupScope Scope { get; set; }

    /// <summary>Planets to look in when <see cref="Scope"/> is Selected.</summary>
    public List<long> PlanetIds { get; set; } = [];

    /// <summary>Include direct messages when <see cref="Scope"/> is Selected.</summary>
    public bool IncludeDirectMessages { get; set; }

    /// <summary>Only these kinds of file. Empty means all kinds.</summary>
    public List<ContentCategory> Categories { get; set; } = [];

    /// <summary>Only files at least this large.</summary>
    public long? MinSizeBytes { get; set; }
}

public class AttachmentCleanupPreview
{
    public int Count { get; set; }
    public long TotalBytes { get; set; }
    public DateTime? Oldest { get; set; }
    public DateTime? Newest { get; set; }
}

/// <summary>
/// How much of an account's storage is used by the files in one place.
/// </summary>
public class AttachmentPlaceUsage
{
    /// <summary>The planet, or null for direct messages.</summary>
    public long? PlanetId { get; set; }
    public int Count { get; set; }
    public long TotalBytes { get; set; }
}

public class AttachmentPlacesResponse
{
    public List<AttachmentPlaceUsage> Places { get; set; } = [];

    /// <summary>Files used by no message or thread.</summary>
    public AttachmentPlaceUsage Unattached { get; set; } = new();
}

/// <summary>
/// A request to build a backup archive of the files a cleanup covers.
/// </summary>
public class AttachmentArchiveRequest
{
    public AttachmentCleanupCriteria Criteria { get; set; }
}

public class AttachmentArchiveTicket
{
    /// <summary>An unguessable id that is also the credential for the download.</summary>
    public string Id { get; set; }

    /// <summary>Where the browser downloads the archive from.</summary>
    public string DownloadPath { get; set; }

    public int Count { get; set; }
    public long TotalBytes { get; set; }
}

public enum AttachmentArchiveState
{
    Pending = 0,
    Streaming = 1,
    Completed = 2,
    Failed = 3,
    Expired = 4,
}

public class AttachmentArchiveStatus
{
    public AttachmentArchiveState State { get; set; }

    /// <summary>How many files could not be read from storage and are not in the archive.</summary>
    public int SkippedCount { get; set; }
}

public class AttachmentCleanupDeleteRequest
{
    public AttachmentCleanupCriteria Criteria { get; set; }

    /// <summary>The backup that was downloaded for these exact criteria.</summary>
    public string ArchiveId { get; set; }

    /// <summary>The user chose to delete without downloading a backup.</summary>
    public bool SkipBackup { get; set; }

    /// <summary>The most files to delete in this request.</summary>
    public int MaxItems { get; set; } = 100;
}

public class AttachmentCleanupDeleteResult
{
    public int Deleted { get; set; }
    public long FreedBytes { get; set; }

    /// <summary>How many matching files are left after this request.</summary>
    public int Remaining { get; set; }
}
