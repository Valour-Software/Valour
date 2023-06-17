using System;
using Valour.Api.Client;
using Valour.Shared.Models;

namespace Valour.Api.Models
{
    public interface IPlanetModel : ISharedItem
    {
        public long PlanetId { get; set; }

        /// <summary>
        /// Returns the planet for this model
        /// </summary>
        public static ValueTask<Planet> GetPlanetAsync(IPlanetModel model, bool refresh = false) =>
            Planet.FindAsync(model.PlanetId, refresh);
    }
}

