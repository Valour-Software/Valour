namespace Valour.Shared.Models;

public class CategoryOrderEvent
{
    public long PlanetId { get; set; }
    public long CategoryId { get; set; }
    public List<long> Order { get; set; }
}