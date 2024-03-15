namespace Valour.Shared.Models;

public class PagedResponse<T>
{
    public static PagedResponse<T> Empty = new PagedResponse<T>()
    {
        Items = new List<T>(),
        TotalCount = 0
    };
    
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
}