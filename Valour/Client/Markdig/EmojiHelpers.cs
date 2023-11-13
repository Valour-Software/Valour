using System.Globalization;
using System.Text;

namespace Valour.Client.Markdig;

public class EmojiHelpers
{
    public static string EmojiToTwemoji(string emoji)
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
        return result != "" ? result : null;
    }
}