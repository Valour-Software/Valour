using Valour.Sdk.Client;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

/// <summary>
/// Shared state for the ambient tutorial hints — small dismissible callouts
/// that replace the old forced click-through tutorial. Hints self-complete
/// when the user performs the action they point at.
/// </summary>
public static class TutorialHints
{
    /// <summary>
    /// Raised when any tutorial bit completes so visible hints can hide
    /// </summary>
    public static event Action StateChanged;

    /// <summary>
    /// Hints stay hidden until first-run onboarding is completed or skipped,
    /// so they never stack on top of the onboarding window
    /// </summary>
    public static bool ShouldShow(ValourClient client, int tutorialId)
    {
        var state = client?.Me?.TutorialState;
        if (state is null)
            return false;

        return UserTutorials.IsCompleted(state.Value, UserTutorials.Onboarding)
               && !UserTutorials.IsCompleted(state.Value, tutorialId);
    }

    public static async Task CompleteAsync(ValourClient client, int tutorialId)
    {
        if (client?.Me is null ||
            UserTutorials.IsCompleted(client.Me.TutorialState, tutorialId))
            return;

        // Optimistic: hide immediately; worst case on a failed save the hint
        // reappears next session
        client.Me.TutorialState = UserTutorials.WithCompleted(client.Me.TutorialState, tutorialId);
        StateChanged?.Invoke();

        await client.PrimaryNode.PostAsync($"api/users/me/tutorials/{tutorialId}/complete", null);
    }
}
