using Markdig.Blazor;
using Valour.Client.Emojis;

namespace Valour.Client.Markdig;

public class ValourEmojiRenderer : BlazorObjectRenderer<ValourEmojiInline>
{
    
    
    protected override void Write(BlazorRenderer renderer, ValourEmojiInline obj)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        /*
        renderer.OpenElement("em-emoji");
        if (obj.Native is not null)
        {
            renderer.AddAttribute("native", obj.Native);
        }
        else
        {
            renderer.AddAttribute("shortcodes", obj.Match);
        }

        renderer.AddAttribute("set", "twitter");
        renderer.CloseElement();
        */
        
        renderer.OpenElement("img")
            .AddAttribute("draggable", "false")
            .AddAttribute("class", "emoji")
            .AddAttribute("alt", obj.Match)
            .AddAttribute("src", EmojiSourceProvider.GetSrcUrlByCodePoints(obj.Native))
            .CloseElement();
    }
}