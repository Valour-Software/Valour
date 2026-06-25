using Valour.Client.Components.DockWindows;
using Valour.Client.Components.Menus.Modals;
using Valour.Client.Components.Menus.Modals.Users.Edit;
using Valour.Client.Components.Windows.ChannelWindows;
using Valour.Client.Components.Windows.ThreadWindows;
using Valour.Client.Toast;
using Valour.Sdk.Client;
using Valour.Shared.Utilities;

namespace Valour.Client.Utility;

/// <summary>
/// Resolves Valour URLs and relative routes into in-app destinations. Powers
/// native notification taps (via <see cref="DeepLinkBridge"/>), in-app Valour
/// link clicks (the markdown link renderer), and the initial deep link the app
/// opens on startup.
/// </summary>
public static class DeepLinkRouter
{
    /// <summary>
    /// Attempts to open the destination described by the given Valour URL/route
    /// at the focused window. Returns true if the link was recognized.
    /// </summary>
    public static async Task<bool> TryNavigateAsync(string urlOrRoute, ValourClient client)
    {
        if (client is null)
            return false;

        if (!ValourRouteParser.TryParse(urlOrRoute, out var route))
            return false;

        try
        {
            // Friends is a modal rather than a window.
            if (route.Type == ValourRouteType.Friends)
            {
                OpenFriends(client);
                return true;
            }

            var content = await BuildContentForRouteAsync(route, client);
            if (content is null)
                return false;

            await WindowService.OpenWindowAtFocused(content);
            return true;
        }
        catch (Exception ex)
        {
            client.Logger?.Log("DeepLinkRouter", $"Failed to open link '{urlOrRoute}': {ex}", "red");
            ToastContainer.Instance?.AddToast(new ToastData(
                "Couldn't Open Link",
                "Check your connection and try again.",
                ToastProgressState.Failure));
            return false;
        }
    }

    /// <summary>
    /// Builds the window content for a route without opening it, so callers can
    /// place it however they need (e.g. the startup path adds it as the initial
    /// tab). Returns null for routes that don't map to a window (such as friends).
    /// </summary>
    public static async Task<WindowContent> BuildContentForRouteAsync(ValourRoute route, ValourClient client)
    {
        if (client is null)
            return null;

        switch (route.Type)
        {
            case ValourRouteType.PlanetChannel:
                return await BuildPlanetChannelContentAsync(route, client);
            case ValourRouteType.DirectChannel:
                return await BuildDirectChannelContentAsync(route, client);
            case ValourRouteType.PlanetThread:
            case ValourRouteType.PlanetThreadFeed:
                return await BuildThreadContentAsync(route, client);
            default:
                return null;
        }
    }

    private static async Task<WindowContent> BuildPlanetChannelContentAsync(ValourRoute route, ValourClient client)
    {
        if (route.PlanetId is null || route.ChannelId is null)
            return null;

        var planet = await client.PlanetService.FetchPlanetAsync(route.PlanetId.Value);
        if (planet is null)
            return null;

        var channel = await client.ChannelService.FetchPlanetChannelAsync(route.ChannelId.Value, planet);
        if (channel is null)
            return null;

        var content = await ChatWindowComponent.GetDefaultContent(channel);
        if (route.MessageId is not null && route.MessageId.Value != 0)
            content.TargetMessageId = route.MessageId;

        return content;
    }

    private static async Task<WindowContent> BuildDirectChannelContentAsync(ValourRoute route, ValourClient client)
    {
        if (route.ChannelId is null)
            return null;

        var channel = await client.ChannelService.FetchDirectChannelAsync(route.ChannelId.Value);
        if (channel is null)
            return null;

        var content = await ChatWindowComponent.GetDefaultContent(channel);
        if (route.MessageId is not null && route.MessageId.Value != 0)
            content.TargetMessageId = route.MessageId;

        return content;
    }

    private static async Task<WindowContent> BuildThreadContentAsync(ValourRoute route, ValourClient client)
    {
        if (route.PlanetId is null)
            return null;

        // Thread feed for the planet (no specific thread)
        if (route.ThreadId is null)
        {
            var planet = await client.PlanetService.FetchPlanetAsync(route.PlanetId.Value);
            if (planet is null)
                return null;

            return ThreadsWindowComponent.GetDefaultContent(planet);
        }

        var thread = await client.ThreadService.FetchThreadAsync(route.PlanetId.Value, route.ThreadId.Value);
        if (thread is null)
            return null;

        return ThreadWindowComponent.GetDefaultContent(thread);
    }

    private static void OpenFriends(ValourClient client)
    {
        ModalInjector.Service.OpenModal<EditUserComponent>(new EditUserComponent.ModalParams()
        {
            StartCategory = "General Settings",
            StartItem = "Friends",
            User = client.Me,
        });
    }
}
