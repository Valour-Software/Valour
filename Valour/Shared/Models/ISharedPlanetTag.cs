namespace Valour.Shared.Models;

public interface ISharedPlanetTag : ISharedModel<long>
{
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public string Slug { get; set; }
}