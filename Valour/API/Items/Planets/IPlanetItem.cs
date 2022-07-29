using System;
using Valour.Api.Client;
using Valour.Shared.Items.Planets;

namespace Valour.Api.Items.Planets
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

