using Valour.Config.Configs;

namespace Valour.Server.Services.GoogleDrive;

/// <summary>
/// Google's OAuth and Drive addresses. When <see cref="GoogleDriveConfig.EmulatorUrl"/>
/// is set, every one of them is served from that address with the same path,
/// so the whole flow can run against a local stand-in for Google.
/// </summary>
public static class GoogleEndpoints
{
    private static string Emulator => GoogleDriveConfig.Current.EmulatorUrl?.Trim().TrimEnd('/');

    private static string Pick(string google, string path) =>
        string.IsNullOrEmpty(Emulator) ? google : Emulator + path;

    public static string Authorize => Pick("https://accounts.google.com/o/oauth2/v2/auth", "/o/oauth2/v2/auth");
    public static string Token => Pick("https://oauth2.googleapis.com/token", "/token");
    public static string Revoke => Pick("https://oauth2.googleapis.com/revoke", "/revoke");
    public static string UserInfo => Pick("https://openidconnect.googleapis.com/v1/userinfo", "/v1/userinfo");
    public static string DriveFiles => Pick("https://www.googleapis.com/drive/v3/files", "/drive/v3/files");
    public static string DriveUpload => Pick("https://www.googleapis.com/upload/drive/v3/files", "/upload/drive/v3/files");
}
