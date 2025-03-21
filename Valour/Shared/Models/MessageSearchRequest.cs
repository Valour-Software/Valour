namespace Valour.Shared.Models;

public class MessageSearchRequest
{
    public string SearchText { get; set; }
    public int Count { get; set; } = 20;
}