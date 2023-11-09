using System.Text;
using Markdig.Helpers;
using Markdig.Parsers;
using Valour.Shared.Models;

namespace Valour.Client.Markdig;

public class MentionParser : InlineParser
{
    public MentionParser()
    {
        OpeningCharacters = new[] { '«' };
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var match = slice.CurrentChar;
        
        // Format:
        // «@x-xxxxxxxx»
        // Example:
        // «@m-12638576793878528» (member with id 12638576793878528)
        // Min length is technically 6, but in reality it will never be less than 22

        MentionType type;
        
        // Ensure @ sign is second character
        if (slice.PeekCharExtra(1) != '@')
        {
            return false;
        }

        // Ensure type of mention is valid (comes after @)
        if (!Mention.CharToMentionType.TryGetValue(slice.PeekCharExtra(2), out type))
        {
            return false;
        }
        
        // Ensure the next character is a dash
        if (slice.PeekCharExtra(3) != '-')
        {
            return false;
        }
        
        // Now we parse the id of the mention target
        // We do this by reading the next characters as long as they are a string, and then parsing the result
        // The max length of a ulong is 20, so we can safely read 20 characters
        StringBuilder idBuilder = new();
        char currentChar = slice.PeekCharExtra(4);
        for (int i = 0; i < 20; i++)
        {
            // First check if the character is the end of the mention
            if (currentChar == '»')
            {
                // We have a valid mention!
                // Now we need to add it to the processor
                // We do this by adding a new mention inline
                // We also need to advance the slice by the length of the mention
                // We do this by adding the length of the mention to the slice's start position
                var mention = new Mention
                {
                    Type = type,
                    TargetId = long.Parse(idBuilder.ToString()),
                };
                processor.Inline = new MentionInline(mention);
                slice.Start += i + 6;
                return true;
            }
            else
            
            if (char.IsDigit(currentChar))
            {
                idBuilder.Append(currentChar);
                currentChar = slice.PeekCharExtra(5 + i);
            }
            else
            {
                break;
            }
        }

        return false;
    }
}