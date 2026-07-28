namespace Valour.Shared.Models;

/// <summary>
/// Bit positions within ISharedUser.TutorialState marking one-time
/// experiences the user has completed (or explicitly skipped).
/// </summary>
public static class UserTutorials
{
    /// <summary>
    /// The first-run onboarding window (interest picker + start path)
    /// </summary>
    public const int Onboarding = 0;

    /// <summary>
    /// Opened a planet's live chat for the first time
    /// </summary>
    public const int OpenPlanetChat = 1;

    /// <summary>
    /// Sent a first chat message
    /// </summary>
    public const int FirstChatMessage = 2;

    /// <summary>
    /// Posted (or dismissed the hint for) a first thread
    /// </summary>
    public const int FirstThread = 3;

    /// <summary>
    /// Discovered the swipe-to-open sidebar on mobile
    /// </summary>
    public const int MobileSidebarSwipe = 4;

    public static bool IsCompleted(long tutorialState, int tutorialId) =>
        (tutorialState & (1L << tutorialId)) != 0;

    public static long WithCompleted(long tutorialState, int tutorialId) =>
        tutorialState | (1L << tutorialId);
}
