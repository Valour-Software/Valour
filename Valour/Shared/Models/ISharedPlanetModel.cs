namespace Valour.Shared.Models;

public interface ISharedPlanetModel
{
    long PlanetId { get; set; }
}

/// <summary>
/// Planet items are items which are owned by a planet
/// </summary>
public interface ISharedPlanetModel<TId> : ISharedPlanetModel, ISharedModel<TId>
{
}
