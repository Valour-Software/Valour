using Markdig;
using Markdig.Blazor;
using Markdig.Extensions.AutoLinks;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Valour.Client.Device;
using Valour.Client.Markdig;
using Markdown = Markdig.Markdown;

namespace Valour.Client.Messages;

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2025 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public static class MarkdownManager
{
    public static BlazorRenderer Renderer;
    public static MarkdownPipeline Pipeline;

    static MarkdownManager()
    {
        RegenPipeline();
    }

    public static void RegenPipeline()
    {
        Pipeline = new MarkdownPipelineBuilder()
            .DisableHtml()
            .UseAutoLinks(options: new AutoLinkOptions()
            {
                OpenInNewWindow = true,
            })
            .UsePipeTables()
            .UseGridTables()
            .UseListExtras()
            .UseEmphasisExtras()
            //.UseEmojiAndSmiley(DevicePreferences.AutoEmoji)
            .UseMentionExtension()
            .UseStockExtension()
            .UseValourEmojiExtension(DevicePreferences.AutoEmoji)
            .Build();

        Renderer = new BlazorRenderer(null, true);
        Renderer.ObjectRenderers.Add(new MentionRenderer());
        Renderer.ObjectRenderers.Add(new StockRenderer());
        Renderer.ObjectRenderers.Add(new ValourEmojiRenderer());

        // Must be inserted ahead of the package's built-in LinkInlineRenderer so
        // Valour links get in-app navigation instead of opening a new tab.
        Renderer.ObjectRenderers.Insert(0, new ValourLinkRenderer());
    }

    /// <summary>
    /// Maximum number of emojis a message can contain and still be rendered
    /// large by the "big emoji" preference.
    /// </summary>
    public const int MaxBigEmojiCount = 5;

    /// <summary>
    /// Returns true if the given message content is made up of nothing but
    /// 1-5 emojis (no other text), and is therefore eligible to be rendered
    /// at heading size when the big-emoji preference is enabled.
    /// </summary>
    public static bool IsEmojiOnlyMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        MarkdownDocument document;

        try
        {
            document = Markdown.Parse(content, Pipeline);
        }
        catch
        {
            return false;
        }

        if (document.Count != 1 || document[0] is not ParagraphBlock { Inline: { } inline })
            return false;

        var emojiCount = 0;

        foreach (var item in inline)
        {
            switch (item)
            {
                case ValourEmojiInline:
                    emojiCount++;
                    break;
                case LineBreakInline:
                    break;
                case LiteralInline literal when literal.Content.IsEmptyOrWhitespace():
                    break;
                default:
                    return false;
            }
        }

        return emojiCount is > 0 and <= MaxBigEmojiCount;
    }

    public static string GetHtml(string content)
    {
        if (content is null)
            return "";

        string markdown = "Error: Message could not be parsed.";

        try
        {
            markdown = Markdown.ToHtml(content, Pipeline);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error parsing message!");
            Console.WriteLine("This may be nothing to worry about, a user may have added an insane table or such.");
            Console.WriteLine(e.Message);
        }
        
        return markdown;
    }
}