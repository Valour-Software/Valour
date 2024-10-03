namespace Valour.Shared.Models;

/// <summary>
/// Planet items are items which are owned by a planet
/// </summary>
public interface ISharedPlanetItem : ISharedItem
{
    long PlanetId { get; set; }
}
