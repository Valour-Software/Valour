namespace Valour.Config.Configs;

/// <summary>
/// Settings for copying people's attachments to their own Google Drive. It
/// needs the Google OAuth client in <see cref="ExternalAuthConfig.Google"/>,
/// with the Drive API enabled for that project. See Docs/GoogleDrive.md.
/// </summary>
public class GoogleDriveConfig
{
    public static GoogleDriveConfig Current { get; private set; } = new();

    public GoogleDriveConfig()
    {
        Current = this;
    }

    /// <summary>Turns the feature off even when Google sign-in is configured.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Days a file stays on Valour after it is copied to Drive. After that
    /// only the Drive copy remains, and Valour reads it back when someone
    /// opens the file.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// For testing only. When set, every Google address (sign-in, token, Drive)
    /// is served from this base address instead, for example a local emulator.
    /// Leave empty in production.
    /// </summary>
    public string? EmulatorUrl { get; set; }

    /// <summary>Files copied per worker pass.</summary>
    public int SyncBatchSize { get; set; } = 20;

    /// <summary>Seconds between worker passes.</summary>
    public int SyncIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Minutes a new file waits before it is copied, so it has been attached
    /// to a message and can be filed under that planet or conversation.
    /// </summary>
    public int AttachGraceMinutes { get; set; } = 10;

    /// <summary>Failed copies are tried again this many times before the file is marked failed.</summary>
    public int MaxAttempts { get; set; } = 6;
}
