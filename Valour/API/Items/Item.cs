using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Api.Client;
using Valour.Shared;
using Valour.Shared.Items;

namespace Valour.Api.Items
{
    public abstract class Item : ISharedItem
    {
        public static string ItemType => nameof(Item);

        public ulong Id { get; set; }
        
        public string Node { get; set; }

        /// <summary>
        /// Returns the item for the given id
        /// </summary>
        /// <typeparam name="T">The type of the target item</typeparam>
        /// <param name="id">The id of the target item</param>
        /// <param name="refresh">If true, the cache will be skipped</param>
        /// <returns>An item of type T</returns>
        public static async Task<T> FindAsync<T>(ulong id, bool refresh = false) where T : Item, ISharedItem
        {
            if (!refresh)
            {
                var cached = ValourCache.Get<T>(id);
                if (cached is not null)
                    return cached;
            }

            var item = await ValourClient.GetJsonAsync<T>($"api/{nameof(T)}/{id}");

            if (item is not null)
                await ValourCache.Put(id, item);

            return item;
        }

        /// <summary>
        /// Attempts to create this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to create</param>
        /// <returns>The result, with the created item (if successful)</returns>
        public static async Task<TaskResult<T>> CreateAsync<T>(T item) where T : Item, ISharedItem
        {
            return await ValourClient.PostAsyncWithResponse<T>($"api/{nameof(T)}", item);
        }

        /// <summary>
        /// Attempts to update this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to update</param>
        /// <returns>The result, with the updated item (if successful)</returns>
        public static async Task<TaskResult<T>> UpdateAsync<T>(T item) where T : Item, ISharedItem
        {
            return await ValourClient.PutAsyncWithResponse<T>($"api/{nameof(T)}/{item.Id}", item);
        }
    }
}
