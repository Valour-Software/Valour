using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Items
{
    /// <summary>
    /// A live model is a model that is updated in real time
    /// </summary>
    public abstract class ClientModel<TSelf, TId> : ISharedModel<TId>
        where TSelf : ClientModel<TSelf, TId> // curiously recurring template pattern
    {
        public static readonly ModelCache<TSelf, TId> Cache = new();
        
        public TId Id { get; set; }
        
        [JsonIgnore]
        public virtual string IdRoute => $"{BaseRoute}/{Id}";

        [JsonIgnore]
        public virtual string BaseRoute => $"api/{GetType().Name}";

        /// <summary>
        /// Ran when this item is updated
        /// </summary>
        public HybridEvent<ModelUpdateEvent> Updated;

        /// <summary>
        /// Ran when this item is deleted
        /// </summary>
        public HybridEvent Deleted;

        /// <summary>
        /// Custom logic on model update
        /// </summary>
        protected virtual Task OnUpdated(ModelUpdateEvent eventData)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Custom logic on model deletion
        /// </summary>
        protected virtual Task OnDeleted()
        {
            return Task.CompletedTask;
        }

        [JsonIgnore]
        public Node Node
        {
            get
            {
                switch (this)
                {
                    case Planet planet: 
                        // Planets have node known
                        return NodeManager.GetNodeFromName(planet.NodeName);
                    case IClientPlanetModel planetItem:
                    {
                        // Doesn't actually have a planet
                        if (planetItem.PlanetId == -1)
                            return ValourClient.PrimaryNode;
                        
                        // Planet items can just check their planet
                        return NodeManager.GetKnownByPlanet(planetItem.PlanetId);
                    }
                    default: 
                        // Everything else can just use the primary node
                        return ValourClient.PrimaryNode;
                }
            }
        }
        
        public virtual async Task AddToCache<T>(bool skipEvent = false)
        {
            await Cache.Put(Id, (TSelf)this, skipEvent);
        }

        /// <summary>
        /// Safely invokes the updated event
        /// </summary>
        public async Task InvokeUpdatedEventAsync(ModelUpdateEvent eventData)
        {
            await OnUpdated(eventData);

            if (Updated != null)
                await Updated.Invoke(eventData);
        }

        /// <summary>
        /// Safely invokes the deleted event
        /// </summary>
        public async Task InvokeDeletedEventAsync()
        {
            await OnDeleted();
            
            if (Deleted != null)
                await Deleted.Invoke();
        }

        /// <summary>
        /// Attempts to create this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to create</param>
        /// <returns>The result, with the created item (if successful)</returns>
        public static async Task<TaskResult<TSelf>> CreateAsync(TSelf item)
        {
            Node node;

            if (item is IClientPlanetModel planetItem)
                node = await NodeManager.GetNodeForPlanetAsync(planetItem.PlanetId);
            else
                node = ValourClient.PrimaryNode;

            return await node.PostAsyncWithResponse<TSelf>(item.BaseRoute, item);
        }

        /// <summary>
        /// Attempts to update this item
        /// </summary>
        /// <typeparam name="T">The type of object being created</typeparam>
        /// <param name="item">The item to update</param>
        /// <returns>The result, with the updated item (if successful)</returns>
        public static async Task<TaskResult<T>> UpdateAsync<T>(T item) where T : ClientModel<TId>
        {
            return await item.Node.PutAsyncWithResponse(item.IdRoute, item);
        }

        /// <summary>
        /// Attempts to delete this item
        /// </summary>
        /// <typeparam name="T">The type of object being deleted</typeparam>
        /// <param name="item">The item to delete</param>
        /// <returns>The result</returns>
        public static async Task<TaskResult> DeleteAsync<T>(T item) where T : ClientModel<TId>
        {
            return await item.Node.DeleteAsync(item.IdRoute);
        }
    }
}
