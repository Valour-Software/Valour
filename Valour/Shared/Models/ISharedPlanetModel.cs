namespace Valour.Shared.Models;

/// <summary>
/// Planet items are items which are owned by a planet
/// </summary>
public interface ISharedPlanetModel<TId> : ISharedModel<TId>
{
    long PlanetId { get; set; }
}
