#nullable enable

namespace Valour.Shared.Queries;

public class QuerySort
{
    public string? Field { get; set; }
    public bool Descending { get; set; }
}

public class QueryOptions
{
    public static QueryOptions Empty = new QueryOptions();
    
    public Dictionary<string, string>? Filters { get; set; } = new();
    public QuerySort? Sort { get; set; } = new();
}

public class QueryRequest
{
    /// <summary>
    /// The number of items to skip (offset)
    /// </summary>
    public int Skip { get; set; }
    
    /// <summary>
    /// The number of items to take (limit)
    /// </summary>
    public int Take { get; set; }
    
    /// <summary>
    /// Filter and sorting options
    /// </summary>
    public QueryOptions? Options { get; set; }
}