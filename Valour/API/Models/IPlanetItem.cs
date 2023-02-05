using System;
using Valour.Api.Client;
using Valour.Shared.Models;

namespace Valour.Api.Models
{
    public interface IPlanetItem
    {
        public long PlanetId { get; set; }

        /// <summary>
        /// Returns the planet for this item
        /// </summary>
        public static ValueTask<Planet> GetPlanetAsync(IPlanetItem item, bool refresh = false) =>
            Planet.FindAsync(item.PlanetId, refresh);
    }
}

