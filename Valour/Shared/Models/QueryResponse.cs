namespace Valour.Shared.Models;

public class QueryResponse<T>
{
    public static QueryResponse<T> Empty = new QueryResponse<T>()
    {
        Items = new List<T>(),
        TotalCount = 0
    };
    
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
}