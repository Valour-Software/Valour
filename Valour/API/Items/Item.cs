using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Planets;
using Valour.Api.Nodes;
using Valour.Shared;
using Valour.Shared.Items;

namespace Valour.Api.Items
{
    public abstract class Item : ISharedItem
    {
        public long Id { get; set; }
        
        public string NodeName { get; set; }

        public Node Node => NodeManager.NameToNode[NodeName];

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

        public virtual async Task AddToCache()
        {
            await ValourCache.Put(this.Id, this);
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
        /// Attempts to create this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to create</param>
        /// <returns>The result, with the created item (if successful)</returns>
        public static async Task<TaskResult<T>> CreateAsync<T>(T item) where T : Item
        {
            Node node;

            if (item is IPlanetItem)
                node = await NodeManager.GetNodeForPlanetAsync(((IPlanetItem)item).PlanetId);
            else
                node = ValourClient.PrimaryNode;

            return await node.PostAsyncWithResponse<T>(item.BaseRoute, item);
        }

        /// <summary>
        /// Attempts to update this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to update</param>
        /// <returns>The result, with the updated item (if successful)</returns>
        public static async Task<TaskResult<T>> UpdateAsync<T>(T item) where T : Item
        {
            return await item.Node.PutAsyncWithResponse<T>(item.IdRoute, item);
        }

        /// <summary>
        /// Attempts to delete this item
        /// </summary>
        /// <typeparam name="T">The type of object being deleted</typeparam>
        /// <param name="item">The item to delete</param>
        /// <returns>The result</returns>
        public static async Task<TaskResult> DeleteAsync<T>(T item) where T : Item
        {
            return await item.Node.DeleteAsync(item.IdRoute);
        }
    }
}
