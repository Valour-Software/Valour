using System;
using Valour.Api.Client;
using Valour.Shared.Items.Planets;

namespace Valour.Api.Items.Planets
{
	public class PlanetItem : Item
	{
        public long PlanetId { get; set; }

        /// <summary>
        /// Returns the planet for this item
        /// </summary>
        public virtual async ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
            await Planet.FindAsync(PlanetId, refresh);

        public override string IdRoute =>
            $"{BaseRoute}/{Id}";

#if (NODES)
        public override string BaseRoute =>
            $"https://{Node}.nodes.valour.gg/api/{nameof(Planet)}/{PlanetId}/{GetType().Name}";
#else
        public override string BaseRoute =>
            $"/api/{nameof(Planet)}/{PlanetId}/{GetType().Name}";
#endif

    }
}

