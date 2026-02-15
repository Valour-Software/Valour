using Markdig.Blazor;
using Valour.Client.Components.Messages;
using Valour.Client.Emojis;
using Valour.Shared.Models;

namespace Valour.Client.Markdig;

public class ValourEmojiRenderer : BlazorObjectRenderer<ValourEmojiInline>
{
    
    
    protected override void Write(BlazorRenderer renderer, ValourEmojiInline obj)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        if (obj.CustomId is not null)
        {
            if (TryGetPlanetId(renderer.RenderContext, out var planetId))
            {
                var alt = obj.Match ?? $"custom-emoji-{obj.CustomId.Value}";
                renderer.OpenElement("img")
                    .AddAttribute("draggable", "false")
                    .AddAttribute("class", "emoji custom-emoji")
                    .AddAttribute("alt", alt)
                    .AddAttribute("title", alt)
                    .AddAttribute("src", ISharedPlanetEmoji.GetCdnUrl(planetId.Value, obj.CustomId.Value))
                    .CloseElement();
                return;
            }

            renderer.WriteText(obj.Match ?? string.Empty);
            return;
        }

        if (!string.IsNullOrWhiteSpace(obj.Native))
        {
            var alt = obj.Match ?? string.Empty;
            renderer.OpenElement("img")
                .AddAttribute("draggable", "false")
                .AddAttribute("class", "emoji")
                .AddAttribute("alt", alt)
                .AddAttribute("title", alt)
                .AddAttribute("src", EmojiSourceProvider.GetSrcUrlByCodePoints(obj.Native))
                .CloseElement();
            return;
        }

        renderer.WriteText(obj.Match ?? string.Empty);
    }

    private static bool TryGetPlanetId(object? renderContext, out long? planetId)
    {
        planetId = null;

        if (renderContext is not MessageComponent messageComponent)
            return false;

        planetId = messageComponent.ParamData?.Message?.PlanetId;
        return planetId is not null;
    }
}
