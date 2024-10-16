using Valour.Shared.Models;

namespace Valour.Sdk.Models
{
    public interface IClientPlanetModel : ISharedModel<long>
    {
        public long PlanetId { get; set; }

        /// <summary>
        /// Returns the planet for this model
        /// </summary>
        public static ValueTask<Planet> GetPlanetAsync(IClientPlanetModel model, bool refresh = false) =>
            Planet.FindAsync(model.PlanetId, refresh);
    }
}

