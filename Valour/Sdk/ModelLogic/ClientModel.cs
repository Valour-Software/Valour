using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.ModelLogic;

public abstract class ClientModel
{
    
}

/// <summary>
/// A live model is a model that is updated in real time
/// </summary>
public abstract class ClientModel<TSelf, TId> : ClientModel, ISharedModel<TId>
    where TSelf : ClientModel<TSelf, TId> // curiously recurring template pattern
    where TId : IEquatable<TId>
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
    public HybridEvent<ModelUpdateEvent<TSelf>> Updated;

    /// <summary>
    /// Ran when this item is deleted
    /// </summary>
    public HybridEvent Deleted;

    /// <summary>
    /// Custom logic on model update
    /// </summary>
    protected virtual void OnUpdated(ModelUpdateEvent<TSelf> eventData) { }

    /// <summary>
    /// Custom logic on model deletion
    /// </summary>
    protected virtual void OnDeleted() { }

    [JsonIgnore] 
    public virtual Node Node => ValourClient.PrimaryNode;
    
    /// <summary>
    /// Pushes this version of this model to cache and optionally
    /// fires off event for the update. Flags can be added for additional data.
    /// Returns the global cached instance of the model.
    /// </summary>
    public virtual TSelf Sync(bool skipEvent = false, int flags = 0)
    {
        var existing = AddToCacheOrReturnExisting();
        return ModelUpdater.UpdateItem<TSelf, TId>((TSelf)this, existing, flags, skipEvent); // Update if already exists
    }

    /// <summary>
    /// Adds this item to the cache. If a copy already exists, it is returned to be updated.
    /// </summary>
    public virtual TSelf AddToCacheOrReturnExisting()
    {
        return Cache.Put(Id, (TSelf)this);
    }
    
    /// <summary>
    /// Returns and removes this item from the cache.
    /// </summary>
    public virtual TSelf TakeAndRemoveFromCache()
    {
        return Cache.TakeAndRemove(Id);
    }

    /// <summary>
    /// Safely invokes the updated event
    /// </summary>
    public void InvokeUpdatedEvent(ModelUpdateEvent<TSelf> eventData)
    {
        OnUpdated(eventData);

        if (Updated != null)
            Updated.Invoke(eventData);
    }

    /// <summary>
    /// Safely invokes the deleted event
    /// </summary>
    public void InvokeDeletedEvent()
    {
        OnDeleted();
        
        if (Deleted != null)
            Deleted.Invoke();
    }

    /// <summary>
    /// Attempts to create this item on the server
    /// </summary>
    /// <returns>The result, with the created item (if successful)</returns>
    public virtual Task<TaskResult<TSelf>> CreateAsync()
    {
        if (!Id.Equals(default(TId)))
            throw new Exception("Trying to create an item with an ID already assigned. Has it already been created?");
            
        return Node.PostAsyncWithResponse<TSelf>(BaseRoute, this);
    }


    /// <summary>
    /// Attempts to update this item on the server
    /// </summary>
    /// <returns>The result, with the updated item (if successful)</returns>
    public virtual Task<TaskResult<TSelf>> UpdateAsync()
    {
        if (Id.Equals(default(TId)))
            throw new Exception("Trying to update an item with no ID assigned. Has it been created?");
        
        return Node.PutAsyncWithResponse<TSelf>(IdRoute, this);
    }

    /// <summary>
    /// Attempts to delete this item on the server
    /// </summary>
    /// <returns>The result</returns>
    public virtual Task<TaskResult> DeleteAsync()
    {
        if (Id.Equals(default(TId)))
            throw new Exception("Trying to delete an item with no ID assigned. Does it exist?");
        
        return Node.DeleteAsync(IdRoute);
    }
}

