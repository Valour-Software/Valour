using Markdig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
                                                    .Build();
        }
    }
}
