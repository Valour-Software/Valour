namespace Valour.Server.Models;

public struct ReactionTotal
{
    public string Emoji;
    public int Count;
    
    public ReactionTotal(string emoji, int count)
    {
        Emoji = emoji;
        Count = count;
    }
}