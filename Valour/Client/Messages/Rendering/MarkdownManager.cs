using Markdig;
using Markdig.Extensions;
using System.Text.RegularExpressions;
using Markdig.Extensions.MediaLinks;
using Valour.Client.Device;
using Valour.Client.Messages;

namespace Valour.Client.Messages;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public static class MarkdownManager
{
    public static MarkdownPipeline pipeline;
    private static readonly MediaOptions MediaOptions = new();

    public static MarkdownPipelineBuilder UseVooperMediaLinks(this MarkdownPipelineBuilder pipeline,
        MediaOptions? options = null)
    {
        if (!pipeline.Extensions.Contains<VooperMediaLinkExtension>())
        {
            pipeline.Extensions.Add(new VooperMediaLinkExtension(options));
        }

        return pipeline;
    }

    static MarkdownManager()
    {
        RegenPipeline();
    }

    public static void RegenPipeline()
    {
        pipeline = new MarkdownPipelineBuilder().DisableHtml()
            //.UseVooperMediaLinks()
            .UseAutoLinks()
            .UseMathematics()
            .UseAbbreviations()
            .UseCitations()
            .UseCustomContainers()
            .UseDiagrams()
            .UseFigures()
            .UseFootnotes()
            .UseGlobalization()
            .UseGridTables()
            .UseListExtras()
            .UsePipeTables()
            .UseTaskLists()
            .UseEmphasisExtras()
            .UseEmojiAndSmiley(DevicePreferences.AutoEmoji)
            .UseReferralLinks("nofollow")
            .UseSoftlineBreakAsHardlineBreak()
            .Build();
    }

    public static readonly Regex sanitizeLink = new("(?<=follow\">).+?(?=<)");

    public static string GetHtml(string content)
    {
        if (content is null)
            return "";

        string markdown = "Error: Message could not be parsed.";

        try
        {
            markdown = Markdown.ToHtml(content, pipeline);
        }
        catch (Exception e)
        {
            Console.WriteLine("Error parsing message!");
            Console.WriteLine("This may be nothing to worry about, a user may have added an insane table or such.");
            Console.WriteLine(e.Message);
        }

        markdown = markdown.Replace("<a", "<a target='_blank'");

        return markdown;
    }
}