using Markdig;
using Markdig.Blazor;
using Markdig.Extensions.AutoLinks;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly Regex UserMentionToken = new("«@[mu]-[0-9]{1,20}»", RegexOptions.Compiled);
    private static readonly Regex ChannelMentionToken = new("«@c-[0-9]{1,20}»", RegexOptions.Compiled);
    private static readonly Regex RoleMentionToken = new("«@r-[0-9]{1,20}»", RegexOptions.Compiled);
    private static readonly Regex CustomEmojiToken = new("«e-:([a-z0-9_]{2,32}):~[0-9]{1,20}»", RegexOptions.Compiled);
    private static readonly Regex StockToken = new("\\$[A-Za-z]{1,6}", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new("\\s+", RegexOptions.Compiled);

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
            // GetHtml() renders through Markdig's default HtmlRenderer, which does
            // not go through ValourLinkRenderer - without this, javascript: links
            // reach embeds, planet descriptions, and rules.
            .UseSafeLinks()
            .UseAutoLinks(options: new AutoLinkOptions()
            {
                OpenInNewWindow = true,
            })
            .UsePipeTables()
            .UseGridTables()
            .UseListExtras()
            .UseEmphasisExtras()
            .UseSpoilerExtension()
            .UseMentionExtension()
            .UseStockExtension()
            .UseValourEmojiExtension(DevicePreferences.AutoEmoji)
            .Build();

        Renderer = new BlazorRenderer(null, true);
        Renderer.ObjectRenderers.Add(new MentionRenderer());
        Renderer.ObjectRenderers.Add(new StockRenderer());
        Renderer.ObjectRenderers.Add(new ValourEmojiRenderer());

        // Must be inserted ahead of the package's built-in EmphasisInlineRenderer -
        // SpoilerInline derives from EmphasisInline to reuse its delimiter-run parsing,
        // but that also means the built-in renderer matches it and would render
        // the spoiler's contents unwrapped, with no span/blur at all.
        Renderer.ObjectRenderers.Insert(0, new SpoilerRenderer());

        // Must be inserted ahead of the package's built-in LinkInlineRenderer so
        // Valour links get in-app navigation instead of opening a new tab.
        Renderer.ObjectRenderers.Insert(0, new ValourLinkRenderer());
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

    /// <summary>
    /// Projects message markdown through the same safe parsing pipeline used by
    /// chat, then flattens it for surfaces that cannot host Blazor components
    /// (for example, village canvas bubbles).
    /// </summary>
    public static string GetPlainText(string content, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(content) || maxLength <= 0)
            return string.Empty;

        // Mentions and custom emoji normally become Blazor components. Replace
        // their wire tokens first so the plain-text renderer has useful labels
        // instead of silently dropping those custom inline nodes.
        var normalized = UserMentionToken.Replace(content, "@user");
        normalized = ChannelMentionToken.Replace(normalized, "#channel");
        normalized = RoleMentionToken.Replace(normalized, "@role");
        normalized = CustomEmojiToken.Replace(normalized, ":$1:");
        normalized = ProtectStockTokens(normalized, out var stockTokens);
        normalized = ProtectNativeEmoji(normalized, out var nativeEmoji);

        string text;
        try
        {
            text = Markdown.ToPlainText(normalized, Pipeline);
        }
        catch
        {
            text = normalized;
        }

        for (var index = 0; index < nativeEmoji.Count; index++)
            text = text.Replace(EmojiPlaceholder(index), nativeEmoji[index], StringComparison.Ordinal);
        for (var index = 0; index < stockTokens.Count; index++)
            text = text.Replace(StockPlaceholder(index), stockTokens[index], StringComparison.Ordinal);

        text = Whitespace.Replace(text, " ").Trim();
        var elementStarts = StringInfo.ParseCombiningCharacters(text);
        if (elementStarts.Length <= maxLength)
            return text;

        var keepElements = Math.Max(0, maxLength - 1);
        var endIndex = keepElements == 0 ? 0 : elementStarts[keepElements];
        return text[..endIndex].TrimEnd() + "\u2026";
    }

    private static string ProtectNativeEmoji(string content, out List<string> emoji)
    {
        emoji = [];
        var result = new StringBuilder(content.Length);
        var elements = StringInfo.GetTextElementEnumerator(content);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (!ContainsEmojiRune(element))
            {
                result.Append(element);
                continue;
            }

            result.Append(EmojiPlaceholder(emoji.Count));
            emoji.Add(element);
        }

        return result.ToString();
    }

    private static string ProtectStockTokens(string content, out List<string> stocks)
    {
        var found = new List<string>();
        var result = StockToken.Replace(content, match =>
        {
            var placeholder = StockPlaceholder(found.Count);
            found.Add(match.Value);
            return placeholder;
        });
        stocks = found;
        return result;
    }

    private static string EmojiPlaceholder(int index) => $"VALOUREMOJITOKEN{index}END";
    private static string StockPlaceholder(int index) => $"VALOURSTOCKTOKEN{index}END";

    private static bool ContainsEmojiRune(string element)
    {
        foreach (var rune in element.EnumerateRunes())
        {
            if (IsEmojiRune(rune))
                return true;
        }

        return false;
    }

    private static bool IsEmojiRune(Rune rune)
    {
        var value = rune.Value;
        return value is 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or
               0x3030 or 0x303D or 0x3297 or 0x3299 or 0xFE0F or 0x200D or 0x20E3 ||
               value is >= 0x2190 and <= 0x21FF ||
               value is >= 0x2300 and <= 0x23FF ||
               value is >= 0x2600 and <= 0x27BF ||
               value is >= 0x2B00 and <= 0x2BFF ||
               value is >= 0x1F000 and <= 0x1FAFF;
    }
}
