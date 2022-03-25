using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Api.Client;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

namespace Valour.Api.Items.Planets
{
    public abstract class PlanetItem<T> : Item, INodeSpecific, ISharedPlanetItem where T : Item
    {
        public ulong Planet_Id { get; set; }
        public string Node { get; set; }

        /// <summary>
        /// Returns a planet item for the given ID.
        /// </summary>
        public virtual async Task<T> FindAsync(ulong id, bool force_refresh)
        {
            T item = null;

            if (!force_refresh)
            {
                item = ValourCache.Get<T>(id);
                if (item is not null)
                    return item;
            }

            item = await ValourClient.GetJsonAsync<T>($"{((INodeSpecific)this).NodeLocation}/planets/{Planet_Id}/{ItemType}/{id}");

            if (item is not null)
                await ValourCache.Put(id, item);

            return item;
        }
            

    }
}
