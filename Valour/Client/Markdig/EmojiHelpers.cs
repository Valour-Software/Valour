using System.Globalization;
using System.Text;

namespace Valour.Client.Markdig;

public class EmojiHelpers
{
    private static readonly Dictionary<string, string> Cache = new();
    
    public static string EmojiToUnified(string emoji)
    {
        if (Cache.TryGetValue(emoji, out var cached))
        {
            return cached;
        }
        else
        {
            var enumerator = StringInfo.GetTextElementEnumerator(emoji);
            var codePoints = new StringBuilder();

            while (enumerator.MoveNext())
            {
                string textElement = enumerator.GetTextElement();
                foreach (var rune in textElement.EnumerateRunes())
                {
                    if (codePoints.Length > 0)
                        codePoints.Append("-");
                    codePoints.AppendFormat("{0:x}", rune.Value);
                }
            }

            string result = codePoints.ToString();
            if (result == "")
                result = null;

            Cache.Add(emoji, result);
            return result;
        }
    }
}