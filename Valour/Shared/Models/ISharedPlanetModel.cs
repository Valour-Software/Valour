namespace Valour.Shared.Models;

/// <summary>
/// Planet items are items which are owned by a planet
/// </summary>
public interface ISharedPlanetModel : ISharedModel
{
    long PlanetId { get; set; }
}
