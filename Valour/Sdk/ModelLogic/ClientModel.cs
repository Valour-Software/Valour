using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.ModelLogic;

/// <summary>
/// Marks a field or method as being ignored when checking for changes
/// when a realtime update is received.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
public class IgnoreRealtimeChangesAttribute : Attribute
{
    
}

public abstract class ClientModel
{
    [IgnoreRealtimeChanges]
    [JsonIgnore]
    public virtual string BaseRoute => $"api/{GetType().Name}";
    
    /// <summary>
    /// Ran when this item is deleted
    /// </summary>
    [IgnoreRealtimeChanges]
    public HybridEvent Deleted;

    /// <summary>
    /// Custom logic on model deletion
    /// </summary>
    protected virtual void OnDeleted() { }
    
    /// <summary>
    /// The Valour Client this model belongs to
    /// </summary>
    [JsonIgnore]
    [IgnoreRealtimeChanges]
    public ValourClient Client { get; private set; }

    /// <summary>
    /// The node this model belongs to
    /// </summary>
    [JsonIgnore]
    [IgnoreRealtimeChanges]
    public virtual Node Node => Client?.PrimaryNode;
    
    [JsonConstructor]
    protected ClientModel() { }
    public ClientModel(ValourClient client)
    {
        SetClient(client);
    }
    
    /// <summary>
    /// Safely invokes the deleted event
    /// </summary>
    public void InvokeDeletedEvent()
    {
        OnDeleted();
        Deleted?.Invoke();
    }
    
    /// <summary>
    /// Sets the client which owns this model
    /// </summary>
    public void SetClient(ValourClient client)
    {
        Client = client;
    }
}

public abstract class ClientModel<TSelf> : ClientModel
    where TSelf : ClientModel<TSelf>
{
    
    /// <summary>
    /// Ran when this item is updated
    /// </summary>
    [IgnoreRealtimeChanges]
    public HybridEvent<ModelUpdatedEvent<TSelf>> Updated;
    
    [JsonConstructor]
    protected ClientModel(): base() { }
    public ClientModel(ValourClient client) : base(client) { }

    /// <summary>
    /// Custom logic on model update
    /// </summary>
    protected virtual void OnUpdated(ModelUpdatedEvent<TSelf> eventData) { }
    
    /// <summary>
    /// Syncs the model, returning the master copy. 
    /// </summary>
    /// <param name="client">The ValourClient to sync to</param>
    /// <param name="flags">Flags to control things like event handling and sorting</param>
    /// <returns>The updated master copy of the model</returns>
    public virtual TSelf Sync(ValourClient client, ModelInsertFlags flags = ModelInsertFlags.None)
    {
        // Set the client
        SetClient(client);
        
        // Sync the sub models
        SyncSubModels(flags);
        
        // Add to cache and return master copy of this model
        return AddToCache(flags);
    }
    
    /// <summary>
    /// Removes the model from cache, and optionally fires off event for the deletion.
    /// </summary>
    public virtual void Destroy(ValourClient client, bool skipEvent = false)
    {
        // Set the client
        // We need to do this because it may be a brand new model
        SetClient(client);
        RemoveFromCache(skipEvent);
    }
    
    /// <summary>
    /// Syncs the sub models of this model
    /// </summary>
    public virtual void SyncSubModels(ModelInsertFlags flags = ModelInsertFlags.None) { }

    /// <summary>
    /// Adds this item to the cache
    /// </summary>
    public abstract TSelf AddToCache(ModelInsertFlags flags = ModelInsertFlags.None);
    
    /// <summary>
    /// Returns and removes this item from the cache.
    /// </summary>
    public abstract TSelf RemoveFromCache(bool skipEvents = false);

    /// <summary>
    /// Safely invokes the updated event
    /// </summary>
    public void InvokeUpdatedEvent(ModelUpdatedEvent<TSelf> eventData)
    {
        OnUpdated(eventData);
        Updated?.Invoke(eventData);
    }
}

/// <summary>
/// A live model is a model that is updated in real time
/// </summary>
public abstract class ClientModel<TSelf, TId> : ClientModel<TSelf>, ISharedModel<TId>
    where TSelf : ClientModel<TSelf, TId> // curiously recurring template pattern
    where TId : IEquatable<TId>
{
    public TId Id { get; set; }
    
    [IgnoreRealtimeChanges]
    [JsonIgnore]
    public virtual string IdRoute => $"{BaseRoute}/{Id}";
    
    [JsonConstructor]
    protected ClientModel(): base() { }
    public ClientModel(ValourClient client) : base(client) { }
    
    /// <summary>
    /// Attempts to create this item on the server
    /// </summary>
    /// <returns>The result, with the created item (if successful)</returns>
    public virtual Task<TaskResult<TSelf>> CreateAsync()
    {
        if (!Id.Equals(default))
            throw new Exception("Trying to create a model with an ID already assigned. Has it already been created?");
            
        return Node.PostAsyncWithResponse<TSelf>(BaseRoute, this);
    }


    /// <summary>
    /// Attempts to update this item on the server
    /// </summary>
    /// <returns>The result, with the updated item (if successful)</returns>
    public virtual Task<TaskResult<TSelf>> UpdateAsync()
    {
        if (Id.Equals(default))
            throw new Exception("Trying to update a model with no ID assigned. Has it been created?");
        
        return Node.PutAsyncWithResponse<TSelf>(IdRoute, this);
    }

    /// <summary>
    /// Attempts to delete this item on the server
    /// </summary>
    /// <returns>The result</returns>
    public virtual Task<TaskResult> DeleteAsync()
    {
        if (Id.Equals(default))
            throw new Exception("Trying to delete a model with no ID assigned. Does it exist?");
        
        return Node.DeleteAsync(IdRoute);
    }
}

