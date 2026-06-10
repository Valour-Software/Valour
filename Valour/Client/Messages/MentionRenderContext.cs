using Valour.Client.Components.Messages;
using Valour.Sdk.Models;

namespace Valour.Client.Messages;

/// <summary>
/// Context used by mention components to resolve planet-scoped targets
/// (members, roles, channels) when rendering rich text. Construct directly
/// for surfaces outside of chat (notifications, previews, etc.); chat
/// messages are adapted automatically via From().
/// </summary>
public class MentionRenderContext
{
    public Planet Planet { get; init; }

    /// <summary>
    /// Normalizes any render context object passed through the markdown
    /// renderer into a MentionRenderContext.
    /// </summary>
    public static MentionRenderContext From(object renderContext)
    {
        switch (renderContext)
        {
            case MentionRenderContext context:
                return context;
            case MessageComponent messageComponent:
            {
                var channel = messageComponent.ParamData.ChatComponent.Channel;
                return new MentionRenderContext
                {
                    Planet = channel.PlanetId is null ? null : channel.Planet
                };
            }
            default:
                return new MentionRenderContext();
        }
    }
}
