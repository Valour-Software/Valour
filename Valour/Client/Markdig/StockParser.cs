using System.Text;
using Markdig.Helpers;
using Markdig.Parsers;

namespace Valour.Client.Markdig;

public class StockParser : InlineParser
{
    public StockParser()
    {
        OpeningCharacters = new[] {'$'};
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        // Format:
        // $xxx
        // Example:
        // $SPY (stock ticker for SPY)
        var tickerBuilder = new StringBuilder();

        // Read characters into tickerBuilder until we hit a space or character that is not a letter.
        // Max length for ticker is 6
        
        char currentChar = slice.PeekCharExtra(1);
        for (int i = 0; i < 6; i++)
        {
            if (currentChar == ' ' || !char.IsLetter(currentChar))
            {
                break;
            }
            
            tickerBuilder.Append(currentChar);
            currentChar = slice.PeekCharExtra(i + 2);
        }
        
        // If we didn't read any characters, then this is not a valid mention
        if (tickerBuilder.Length == 0)
        {
            return false;
        }
        
        // We have a valid mention, so we can now advance the slice and add the mention to the processor
        slice.Start += tickerBuilder.Length + 1;
        
        StockInline inline = new(tickerBuilder.ToString());
        processor.Inline = inline;

        return true;
    }
}