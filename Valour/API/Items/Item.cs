using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Planets;
using Valour.Shared;
using Valour.Shared.Items;

namespace Valour.Api.Items
{
    public abstract class Item : ISharedItem
    {
        public long Id { get; set; }
        
        public string Node { get; set; }

        // The *static* field _ItemType on the server is serialized into itemType,
        // Which is then deserialized into this *non-static* property, allowing us
        // to determine item type
        [JsonPropertyName("itemType")]
        public string ItemType { get; set; }

        public virtual string IdRoute => $"{BaseRoute}/{Id}";
        public virtual string BaseRoute => $"/api/{GetType().Name}";

        /// <summary>
        /// Ran when this item is updated
        /// </summary>
        public event Func<int, Task> OnUpdated;

        /// <summary>
        /// Ran when this item is deleted
        /// </summary>
        public event Func<Task> OnDeleted;

        /// <summary>
        /// Custom logic on item update
        /// </summary>
        public virtual async Task OnUpdate(int flags)
        {

        }

        /// <summary>
        /// Safely invokes the updated event
        /// </summary>
        public async Task InvokeUpdatedEventAsync(int flags)
        {
            await OnUpdate(flags);

            if (OnUpdated != null)
                await OnUpdated?.Invoke(flags);
        }

        /// <summary>
        /// Safely invokes the deleted event
        /// </summary>
        public async Task InvokeDeletedEventAsync()
        {
            if (OnDeleted != null)
                await OnDeleted?.Invoke();
        }

        /// <summary>
        /// Returns the item for the given id
        /// </summary>
        /// <typeparam name="T">The type of the target item</typeparam>
        /// <param name="id">The id of the target item</param>
        /// <param name="refresh">If true, the cache will be skipped</param>
        /// <returns>An item of type T</returns>
        public static async Task<T> FindAsync<T>(object id, bool refresh = false) where T : Item 
        {
            if (!refresh)
            {
                var cached = ValourCache.Get<T>(id);
                if (cached is not null)
                    return cached;
            }

            var item = await ValourClient.GetJsonAsync<T>($"api/{typeof(T).Name}/{id}");

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
        public static async Task<TaskResult<T>> CreateAsync<T>(T item) where T : Item
        {
#if DEBUG
            return await ValourClient.PostAsyncWithResponse<T>($"api/{typeof(T).Name}", item);
#else
            return await ValourClient.PostAsyncWithResponse<T>(
                $"https://{item.Node}.nodes.valour.gg/api/{nameof(T)}", item);
#endif
        }

        /// <summary>
        /// Attempts to update this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to update</param>
        /// <returns>The result, with the updated item (if successful)</returns>
        public static async Task<TaskResult<T>> UpdateAsync<T>(T item) where T : Item
        {
#if DEBUG
            return await ValourClient.PutAsyncWithResponse<T>($"api/{typeof(T).Name}/{item.Id}", item);
#else
            return await ValourClient.PutAsyncWithResponse<T>(
                $"https://{item.Node}.nodes.valour.gg/api/{nameof(T)}/{item.Id}", item);
#endif
        }

        /// <summary>
        /// Attempts to delete this item
        /// </summary>
        /// <typeparam name="T">The type of object being deleted</typeparam>
        /// <param name="item">The item to delete</param>
        /// <returns>The result</returns>
        public static async Task<TaskResult> DeleteAsync<T>(T item) where T : Item
        {
#if DEBUG
            return await ValourClient.DeleteAsync($"api/{typeof(T).Name}/{item.Id}");
#else
            return await ValourClient.DeleteAsync(
                $"https://{item.Node}.nodes.valour.gg/api/{nameof(T)}/{item.Id}");
#endif
        }
    }
}
