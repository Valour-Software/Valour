using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.ModelLogic;

public abstract class ClientModel
{
    [JsonIgnore]
    public virtual string BaseRoute => $"api/{GetType().Name}";
    
    /// <summary>
    /// Ran when this item is deleted
    /// </summary>
    public HybridEvent Deleted;

    /// <summary>
    /// Custom logic on model deletion
    /// </summary>
    protected virtual void OnDeleted() { }
    
    /// <summary>
    /// The Valour Client this model belongs to
    /// </summary>
    public ValourClient Client { get; private set; }

    /// <summary>
    /// The node this model belongs to
    /// </summary>
    public virtual Node Node => Client?.PrimaryNode;

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
    public HybridEvent<ModelUpdateEvent<TSelf>> Updated;

    /// <summary>
    /// Custom logic on model update
    /// </summary>
    protected virtual void OnUpdated(ModelUpdateEvent<TSelf> eventData) { }
    
    /// <summary>
    /// Adds this item to the cache. If a copy already exists, it is returned to be updated.
    /// </summary>
    public abstract TSelf AddToCacheOrReturnExisting();

    /// <summary>
    /// Returns and removes this item from the cache.
    /// </summary>
    public abstract TSelf TakeAndRemoveFromCache();

    /// <summary>
    /// Safely invokes the updated event
    /// </summary>
    public void InvokeUpdatedEvent(ModelUpdateEvent<TSelf> eventData)
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
    
    [JsonIgnore]
    public virtual string IdRoute => $"{BaseRoute}/{Id}";
    
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

