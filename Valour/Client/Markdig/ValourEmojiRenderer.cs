using Markdig.Blazor;

namespace Valour.Client.Markdig;

public class ValourEmojiRenderer : BlazorObjectRenderer<ValourEmojiInline>
{
    private static Dictionary<string, string> _srcUrlCache = new();
    
    public string GetSrcUrl(string emoji)
    {
        if (_srcUrlCache.TryGetValue(emoji, out string url))
            return url;
        
        url = $"https://cdn.jsdelivr.net/npm/emoji-datasource-twitter@14.0.0/img/twitter/64/{emoji}.png";
        _srcUrlCache[emoji] = url;
        
        return url;
    }
    
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
            .AddAttribute("src", GetSrcUrl(obj.Native))
            .CloseElement();
    }
}