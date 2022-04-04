using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Api.Client;
using Valour.Api.Nodes;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

namespace Valour.Api.Items.Planets
{
    public abstract class PlanetItem<T> : Item, INodeSpecific, ISharedPlanetItem where T : Item
    {
        public ulong Planet_Id { get; set; }
        public string Node { get; set; }

        /// <summary>
        /// This is used to get a reference to ItemType
        /// </summary>
        public static T DummyItem = default(T);

        /// <summary>
        /// Returns a planet item for the given IDs.
        /// </summary>
        public static async Task<T> FindAsync(ulong id, ulong planet_id, bool force_refresh)
        {
            T item = null;

            if (!force_refresh)
            {
                item = ValourCache.Get<T>(id);
                if (item is not null)
                    return item;
            }

            item = await ValourClient.GetJsonAsync<T>($"{NodeManager.GetLocation(planet_id)}/planets/{planet_id}/{DummyItem.ItemType}/{id}");

            if (item is not null)
                await ValourCache.Put(id, item);

            return item;
        }
        
        /// <summary>
        /// Applies changes to this planet item.
        /// </summary>
        public virtual async Task<TaskResult> UpdateAsync()
        {

        }
    }
}
