using Valour.Shared.Models;
using Markdig.Helpers;

namespace Valour.Server.Utilities;

/// <summary>
/// The server mention parser has a simple task: Return a list of all the mentions present in a string.
/// </summary>
public static class MentionParser
{
    public static List<Mention> Parse(string text)
    {
        List<Mention> mentions = null;
        
        int pos = 0;
        
        while (pos < text.Length)
        {
            if (text[pos] != '«')
            {
                pos++;
                continue;
            }
            
            var remainingLength = text.Length - pos;
            
            // Must be at least this long ( «@x-xxxxxxxxx...» )
            if (remainingLength < 20)
            {
                pos++;
                continue;
            }
            
            // Mentions (<@x-)
            if (text[pos + 1] != '@' ||
                text[pos + 3] != '-')
            {
                pos++;
                continue;
            }
            
            // Ensure type of mention is valid (comes after @)

            if (!Mention.CharToMentionType.TryGetValue(text[pos + 2], out var type))
            {
                pos++;
                continue;
            }
            
            // Extract id
            char c = ' ';
            int offset = 4;
            string idChars = "";
            while (offset < remainingLength &&
                   (c = text[pos + offset]).IsDigit())
            {
                idChars += c;
                offset++;
            }
            // Make sure ending tag is '>'
            if (c != '»')
            {
                pos++;
                continue;
            }
            if (string.IsNullOrWhiteSpace(idChars))
            {
                pos++;
                continue;
            }
            bool parsed = long.TryParse(idChars, out long id);
            if (!parsed)
            {
                pos++;
                continue;
            }
            // Create object
            Mention mention = new()
            {
                TargetId = id,
                Type = type,
            };

            if (mentions is null)
                mentions = new();
            
            mentions.Add(mention);
            
            // Shift forward by the length of the mention
            pos += offset;
        }
        
        return mentions;
    }
}