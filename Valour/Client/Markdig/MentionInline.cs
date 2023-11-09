using Markdig.Syntax.Inlines;
using Valour.Shared.Models;

namespace Valour.Client.Markdig;

public class MentionInline : LeafInline
{
    public Mention Mention { get; set;}
    
    public MentionInline(Mention mention)
    {
        Mention = mention;
    }
}