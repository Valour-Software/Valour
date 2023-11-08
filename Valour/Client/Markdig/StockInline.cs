using Markdig.Syntax.Inlines;

namespace Valour.Client.Markdig;

public class StockInline : LeafInline
{
    public string Symbol { get; set; }
    
    public StockInline(string symbol)
    {
        Symbol = symbol;
    }
}