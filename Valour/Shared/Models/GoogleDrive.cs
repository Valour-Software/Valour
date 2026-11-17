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

    /// <summary>
    /// The owner chose to keep the file on Valour. It is not copied to Drive
    /// automatically and its Valour copy does not expire. It may still have an
    /// older copy in Drive from before it was moved back.
    /// </summary>
    Pinned = 5,
}

/// <summary>Where an attachment lives, as shown on its row in Attachments settings.</summary>
public enum AttachmentStorageLocation
{
    /// <summary>Only on Valour.</summary>
    Valour = 0,

    /// <summary>Waiting to be sent to Drive, or being sent.</summary>
    SendingToDrive = 1,

    /// <summary>In Drive. Valour serves it to the chat from there as needed.</summary>
    InDrive = 2,

    /// <summary>Sending to Drive failed.</summary>
    DriveFailed = 3,
}

/// <summary>
/// An account's Google Drive connection and how its files are moving into it.
/// </summary>
public class DriveConnectionInfo
{
    /// <summary>True when this server can connect Google Drive at all.</summary>
    public bool Available { get; set; }

    public bool Connected { get; set; }

    /// <summary>
    /// Where new uploads go. True sends them to Drive. False keeps them on Valour,
    /// where they count toward the storage allowance.
    /// </summary>
    public bool NewUploadsToDrive { get; set; }

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

    /// <summary>Older files that were uploaded before Drive was connected.</summary>
    public int UnsyncedCount { get; set; }
    public long UnsyncedBytes { get; set; }

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

/// <summary>Chooses where new uploads go while Drive is connected.</summary>
public class DriveDefaultRequest
{
    public bool NewUploadsToDrive { get; set; }
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
