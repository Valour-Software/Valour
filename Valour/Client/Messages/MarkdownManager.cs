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

            pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions()
                                                    .DisableHtml()
                                                    .UseEmojiAndSmiley(true)
                                                    .UseReferralLinks("nofollow")
                                                    .Build();
        }

        public static Regex sanitizeLink = new Regex("(?<=follow\">).+?(?=<)");

        public static MarkupString GetHtml(string content)
        {
            string markdown = Markdown.ToHtml(content, pipeline);

            markdown = markdown.Replace("<a", "<a target='_blank'");

            //markdown = sanitizeLink.Replace(markdown, "");

            return (MarkupString)markdown;
        }
    }
}
