namespace Valour.Shared.Models;

public struct PagedModelResponse<T>
{
    public List<T> Items { get; set; }
    public int TotalCount { get; set; }
}