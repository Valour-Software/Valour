using Markdig;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Valour.Client.Messages
{
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

        public static Regex sanitizeLink = new Regex("(?<=follow\">).+?(?=<)");

        public static MarkupString GetHtml(string content)
        {
            string markdown = "Error: Message could not be parsed.";

            try
            {
                markdown = Markdown.ToHtml(content, pipeline);
            }
            catch (System.Exception e)
            {
                Console.WriteLine("Error parsing message!");
                Console.WriteLine("This may be nothing to worry about, a user may have added an insane table or such.");
                Console.WriteLine(e.Message);
            }

            markdown = markdown.Replace("<a", "<a target='_blank'");

            //markdown = sanitizeLink.Replace(markdown, "");

            return (MarkupString)markdown;
        }
    }
}
