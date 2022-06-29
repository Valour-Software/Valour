using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Shared;
using Valour.Shared.Items;

namespace Valour.Api.Items
{
    public abstract class Item<T> : ISharedItem where T : class, ISharedItem
    {
        public static string _ItemType => nameof(Item<T>);

        public ulong Id { get; set; }
        
        public string Node { get; set; }

        // The *static* field _ItemType on the server is serialized into itemType,
        // Which is then deserialized into this *non-static* property, allowing us
        // to determine item type
        [JsonPropertyName("itemType")]
        public string ItemType { get; set; }

        public virtual string IdRoute => $"{BaseRoute}/{Id}";
        public virtual string BaseRoute => $"/api/{GetType().Name}";


        /// <summary>
        /// Returns the item for the given id
        /// </summary>
        /// <typeparam name="T">The type of the target item</typeparam>
        /// <param name="id">The id of the target item</param>
        /// <param name="refresh">If true, the cache will be skipped</param>
        /// <returns>An item of type T</returns>
        public static async Task<T> FindAsync(object id, bool refresh = false)
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
        public static async Task<TaskResult<T>> CreateAsync(T item)
        {
            return await ValourClient.PostAsyncWithResponse<T>($"api/{nameof(T)}", item);
        }

        /// <summary>
        /// Attempts to update this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to update</param>
        /// <returns>The result, with the updated item (if successful)</returns>
        public static async Task<TaskResult<T>> UpdateAsync(T item)
        {
            return await ValourClient.PutAsyncWithResponse<T>($"api/{nameof(T)}/{item.Id}", item);
        }

        /// <summary>
        /// Attempts to delete this item
        /// </summary>
        /// <typeparam name="T">The type of object being deleted</typeparam>
        /// <param name="item">The item to delete</param>
        /// <returns>The result</returns>
        public static async Task<TaskResult> DeleteAsync(T item)
        {
            return await ValourClient.DeleteAsync($"api/{nameof(T)}/{item.Id}");
        }
    }
}
