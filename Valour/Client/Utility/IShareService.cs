namespace Valour.Client.Utility;

/// <summary>
/// Platform abstraction for sharing links/text. The browser implementation uses
/// the Web Share API; native hosts (MAUI) present the OS share sheet.
/// </summary>
public interface IShareService
{
    /// <summary>
    /// Attempts to present a share UI for the given content.
    /// Returns true if the platform handled the share (including user cancellation),
    /// false if no share UI is available and the caller should fall back (e.g. copy link).
    /// </summary>
    Task<bool> ShareAsync(string title, string text, string url);
}
