using Markdig.Blazor;
using Valour.Client.Components.Messages;
using Valour.Shared.Models;

namespace Valour.Client.Markdig;

public class MentionRenderer : BlazorObjectRenderer<MentionInline>
{
    protected override void Write(BlazorRenderer renderer, MentionInline obj)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        if (obj.Mention is null)
            return;
        
        switch (obj.Mention.Type)
        {
            case MentionType.User:
                renderer.OpenComponent<UserMentionComponent>()
                    .AddComponentParam("Mention", obj.Mention)
                    .CloseComponent();
                break;
            case MentionType.PlanetMember:
                renderer.OpenComponent<MemberMentionComponent>()
                    .AddComponentParam("Mention", obj.Mention)
                    .CloseComponent();
                break;
            case MentionType.Role:
                renderer.OpenComponent<RoleMentionComponent>()
                    .AddComponentParam("Mention", obj.Mention)
                    .CloseComponent();
                break;
            case MentionType.Channel:
                renderer.OpenComponent<ChannelMentionComponent>()
                    .AddComponentParam("Mention", obj.Mention)
                    .CloseComponent();
                break;
        }
    }
}