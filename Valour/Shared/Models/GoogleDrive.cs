namespace Valour.Shared.Models;

/// <summary>
/// Whether a connected Google Drive can still be used. A connection needs
/// the person to reconnect when Google has revoked or expired its access.
/// </summary>
public enum DriveConnectionStatus
{
    Active = 0,
    NeedsReconnect = 1,
}

/// <summary>Where an uploaded file is in its copy to the owner's Google Drive.</summary>
public enum DriveSyncState
{
    /// <summary>Not queued. The file has no Drive copy, or its owner has no Drive.</summary>
    None = 0,

    /// <summary>Waiting to be copied.</summary>
    Pending = 1,

    /// <summary>A worker is copying it now.</summary>
    Syncing = 2,

    /// <summary>The Drive copy exists. The Valour copy expires after a while.</summary>
    Synced = 3,

    /// <summary>Copying keeps failing. The Valour copy is kept.</summary>
    Failed = 4,
}

/// <summary>
/// An account's Google Drive connection and how its files are moving into it.
/// </summary>
public class DriveConnectionInfo
{
    /// <summary>True when this server can connect Google Drive at all.</summary>
    public bool Available { get; set; }

    public bool Connected { get; set; }

    /// <summary>The Google account the Drive belongs to.</summary>
    public string Email { get; set; }

    public DriveConnectionStatus Status { get; set; }

    public DateTime? LastSyncAt { get; set; }

    /// <summary>A short reason the last copy failed, when one did.</summary>
    public string LastError { get; set; }

    /// <summary>Files with a Drive copy.</summary>
    public int SyncedCount { get; set; }
    public long SyncedBytes { get; set; }

    /// <summary>Files waiting to be copied.</summary>
    public int PendingCount { get; set; }

    /// <summary>Files that could not be copied after several tries.</summary>
    public int FailedCount { get; set; }

    /// <summary>Files that are now only in Drive. Valour's own copy has expired.</summary>
    public int DriveOnlyCount { get; set; }
    public long DriveOnlyBytes { get; set; }

    /// <summary>Older files that were uploaded before Drive was connected.</summary>
    public int UnsyncedCount { get; set; }
    public long UnsyncedBytes { get; set; }

    /// <summary>
    /// Days a file stays on Valour after it is copied to Drive. After that
    /// it is served from Drive.
    /// </summary>
    public int RetentionDays { get; set; }

    /// <summary>The per-file upload limit while Drive is connected, at least.</summary>
    public long MinUploadBytes { get; set; }
}

/// <summary>Finishes connecting Google Drive with the result of the Google consent page.</summary>
public class DriveConnectRequest
{
    public string Ticket { get; set; }

    /// <summary>The verifier whose hash started the flow.</summary>
    public string Verifier { get; set; }
}

public class DriveQueueResult
{
    /// <summary>How many files were queued to be copied.</summary>
    public int Queued { get; set; }
}

/// <summary>Constants shared by the Google Drive screens and the server.</summary>
public static class GoogleDriveLimits
{
    /// <summary>
    /// The least per-file upload limit an account has while Google Drive is
    /// connected. A higher subscription limit still wins.
    /// </summary>
    public const long ConnectedMaxUploadBytes = 100L * 1024 * 1024;
}
