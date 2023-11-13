using Markdig.Blazor;

namespace Valour.Client.Markdig;

public class ValourEmojiRenderer : BlazorObjectRenderer<ValourEmojiInline>
{
    public string GetSrcUrl(string emoji)
    {
        return $"https://twemoji.maxcdn.com/v/latest/svg/{emoji}.svg";
    }
    
    protected override void Write(BlazorRenderer renderer, ValourEmojiInline obj)
    {
        Console.WriteLine("HELLO");
        
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (obj == null) throw new ArgumentNullException(nameof(obj));

        // Outer span
        renderer.OpenElement("img")
            .AddAttribute("draggable", "false")
            .AddAttribute("class", "emoji")
            .AddAttribute("alt", obj.Match)
            .AddAttribute("src", GetSrcUrl(obj.Twemoji))
            .CloseElement();
    }
}