using Markdig;
using Markdig.Blazor;
using Markdig.Extensions.AutoLinks;
using Valour.Client.Device;
using Valour.Client.Markdig;
using Markdown = Markdig.Markdown;

namespace Valour.Client.Messages;

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2024 Valour Software LLC
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