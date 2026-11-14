using Microsoft.JSInterop;
using Valour.Client.Utility;

namespace Valour.Client.Components.Messages;

/// <summary>
/// Shows who reacted to a message when a reaction is hovered. Reactions carry
/// no hover handlers of their own: one document listener in main.js finds the
/// hovered reaction and calls <see cref="ShowAsync"/> with the key of the
/// message that rendered it.
/// </summary>
public static class ReactionTooltips
{
    private static readonly Lock SyncRoot = new();
    private static readonly Dictionary<int, MessageComponent> Hosts = new();
    private static int _nextKey;

    internal static int Register(MessageComponent host)
    {
        lock (SyncRoot)
        {
            var key = ++_nextKey;
            Hosts[key] = host;
            return key;
        }
    }

    internal static void Unregister(int key)
    {
        lock (SyncRoot)
            Hosts.Remove(key);
    }

    [JSInvokable("ShowReactionTooltip")]
    public static Task ShowAsync(int hostKey, string emoji, ElementBounds bounds)
    {
        MessageComponent host;
        lock (SyncRoot)
        {
            if (!Hosts.TryGetValue(hostKey, out host))
                return Task.CompletedTask;
        }

        return host.ShowReactionTooltipAsync(emoji, bounds);
    }
}
