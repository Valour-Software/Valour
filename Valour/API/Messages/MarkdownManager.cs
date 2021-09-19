using Markdig;
using System.Text.RegularExpressions;

namespace Valour.Api.Messages;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public static class MarkdownManager
{
    public static MarkdownPipeline pipeline;

    static MarkdownManager()
    {

        pipeline = new MarkdownPipelineBuilder().DisableHtml()
                                                .UseAutoLinks()
                                                .UseMediaLinks()
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
                                                .UseEmojiAndSmiley(true)
                                                .UseReferralLinks("nofollow")
                                                .UseSoftlineBreakAsHardlineBreak()
                                                .Build();
    }

    public static readonly Regex sanitizeLink = new("(?<=follow\">).+?(?=<)");

    public static string GetHtml(string content)
    {
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

