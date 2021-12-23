using Markdig;
using Microsoft.AspNetCore.Html;

namespace Valour.Web
{
    public static class MarkdownToHtml
    {
        public static HtmlString Faq;

        public static void LoadMarkdown()
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseBootstrap().Build();
            
            var file = File.ReadAllLines("FAQ.md").ToList();
            var headers = file.Select((Value, Index) => (Value, Index)).Where(x => x.Value.StartsWith("## ")).ToList();

            file.Insert(2, "<table class=\"table\"><thead><tr><th>Content</th></tr></thead><tbody>");
            int i = 3;
            foreach((string Value, int Index) in headers)
            {
                string header = Value.Replace("## ", "");
                file.Insert(i, $"<tr><td><a href=\"#{header.ToLower().Replace(' ', '-')}\">{header}</a></td></tr>");
                i++;
            }
            file.Insert(i, "</tbody></table>");
            file.Insert(i + 1, "");

            //insert content inserter here

            var markdown = Markdown.ToHtml(string.Join('\n', file), pipeline);
            Faq = new HtmlString(markdown);
        }
    }
}
