namespace Valour.Shared.Models;

public class RoleOrderEvent
{
    public long PlanetId { get; set; }
    public List<long> Order { get; set; }
}