#nullable  enable

using System.Collections;
using System.Runtime.CompilerServices;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.ModelLogic;

public enum ModelInsertFlags
{
    None =        0b0000,
    SkipEvents =  0b0001,
    SkipSorting = 0b0010,
    
    /// <summary>
    /// Skips events and sorting
    /// </summary>
    Batched =     0b0011,
}

public class ModelStore<TModel, TId> : IEnumerable<TModel>, IDisposable
    where TModel : ClientModel<TModel, TId>
    where TId : IEquatable<TId>
{
    // Called for all changes
    public HybridEvent<IModelEvent<TModel>>? Changed; // We don't assign because += and -= will do it

    // Specific events
    public HybridEvent<ModelsSetEvent<TModel>>? ModelsSet;
    public HybridEvent<ModelsClearedEvent<TModel>>? ModelsCleared;
    public HybridEvent<ModelsOrderedEvent<TModel>>? ModelsReordered;

    public HybridEvent<ModelAddedEvent<TModel>>? ModelAdded;
    public HybridEvent<ModelUpdatedEvent<TModel>>? ModelUpdated;
    public HybridEvent<ModelRemovedEvent<TModel>>? ModelDeleted;

    protected readonly List<TModel> List;
    // Community nodes own their local object-id space. A non-null scope keeps
    // two same-id external models from different origins distinct while the
    // official network continues to use the existing unscoped fast path.
    protected readonly Dictionary<ModelStoreKey, TModel> IdMap;

    protected readonly record struct ModelStoreKey(TId Id, string? Scope);

    /// <summary>
    /// Lock object for thread-safe access to List and IdMap.
    /// SignalR callbacks run on background threads while UI accesses from main thread.
    /// </summary>
    protected readonly object SyncLock = new();

    public int Count
    {
        get
        {
            lock (SyncLock)
            {
                return List.Count;
            }
        }
    }
    
    public ModelStore(List<TModel>? startingList = null)
    {
        List = startingList ?? new List<TModel>();
        IdMap = List.ToDictionary(x => new ModelStoreKey(x.Id, null));
    }
    
    // Make iterable - returns a snapshot to avoid holding lock during iteration
    public TModel this[int index]
    {
        get
        {
            lock (SyncLock)
            {
                return List[index];
            }
        }
    }

    /// <summary>
    /// Returns an enumerator over a snapshot of the list.
    /// This is thread-safe but the snapshot may be slightly stale.
    /// </summary>
    public IEnumerator<TModel> GetEnumerator()
    {
        List<TModel> snapshot;
        lock (SyncLock)
        {
            snapshot = new List<TModel>(List);
        }
        return snapshot.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    protected virtual ModelUpdatedEvent<TModel> HandleChanges(TModel existing, TModel updated)
    {
        var changes = ModelUpdateUtils.CopyChanges(existing, updated);

        return changes is null ?
            new ModelUpdatedEvent<TModel>(existing, null) :
            new ModelUpdatedEvent<TModel>(existing, new ModelChange<TModel>(changes));
    }

    /// <summary>
    /// Places a newly added model in the list. Called while holding the lock.
    /// </summary>
    protected virtual void InsertNew(TModel model, ModelInsertFlags flags)
    {
        List.Add(model);
    }

    protected int IndexOfReference(TModel model)
    {
        for (var i = 0; i < List.Count; i++)
        {
            if (ReferenceEquals(List[i], model))
                return i;
        }

        return -1;
    }

    public TModel? Put(TModel model, ModelInsertFlags flags = ModelInsertFlags.None)
    {
        var result = PutInternal(model, flags, null);
        return result?.GetModel();
    }

    /// <summary>
    /// Inserts a model under an origin scope. Scoped identifiers are used for
    /// local objects received from federated community nodes.
    /// </summary>
    public TModel? Put(TModel model, ModelInsertFlags flags, string? scope)
    {
        var result = PutInternal(model, flags, scope);
        return result?.GetModel();
    }
    
    protected virtual IModelInsertionEvent<TModel>? PutInternal(TModel? model, ModelInsertFlags flags, string? scope)
    {
        if (model is null)
            return null;

        IModelInsertionEvent<TModel>? result;
        bool isUpdate;
        TModel? existing = null;

        lock (SyncLock)
        {
            var key = new ModelStoreKey(model.Id, scope);
            if (IdMap.TryGetValue(key, out var existingModel))
            {
                existing = existingModel;
                isUpdate = true;
                // Apply changes while holding lock
                var modelEventData = HandleChanges(existing, model);

                // Check if nothing changed
                if (modelEventData.Changes is null)
                {
                    return modelEventData;
                }

                result = modelEventData;
            }
            else
            {
                isUpdate = false;
                InsertNew(model, flags);
                IdMap[key] = model;
                result = new ModelAddedEvent<TModel>(model);
            }
        }

        // Fire events outside lock to prevent deadlocks
        if (!flags.HasFlag(ModelInsertFlags.SkipEvents))
        {
            if (isUpdate && existing is not null)
            {
                var updateEvent = (ModelUpdatedEvent<TModel>)result;
                existing.InvokeUpdatedEvent(updateEvent);
                ModelUpdated?.Invoke(updateEvent);
            }
            else
            {
                var addedEvent = (ModelAddedEvent<TModel>)result;
                ModelAdded?.Invoke(addedEvent);
            }
            Changed?.Invoke(result);
        }

        return result;
    }
    
    public TModel? Remove(TModel item, bool skipEvent = false)
    {
        return Remove(item.Id, null, skipEvent);
    }

    public TModel? Remove(TModel item, string? scope, bool skipEvent = false)
    {
        return Remove(item.Id, scope, skipEvent);
    }
    
    public TModel? Remove(TId id, bool skipEvent = false)
    {
        return Remove(id, null, skipEvent);
    }

    public TModel? Remove(TId id, string? scope, bool skipEvent = false)
    {
        TModel? item = null;

        lock (SyncLock)
        {
            var key = new ModelStoreKey(id, scope);
            if (!IdMap.TryGetValue(key, out item))
                return null;

            IdMap.Remove(key);

            // Get index of item in list
            var index = IndexOfReference(item);
            if (index >= 0)
                List.RemoveAt(index);
        }

        if (item is null)
            return null;

        // Fire events outside lock to prevent deadlocks
        if (!skipEvent)
        {
            item.InvokeDeletedEvent();
            var storeEvent = new ModelRemovedEvent<TModel>(item);
            ModelDeleted?.Invoke(storeEvent);
            Changed?.Invoke(storeEvent);
        }

        return item;
    }
    
    public virtual void Set(List<TModel> items, bool skipEvent = false)
    {
        lock (SyncLock)
        {
            // We clear rather than replace the list to ensure that the reference is maintained
            // Because the reference may be used across the application.
            List.Clear();
            IdMap.Clear();

            List.AddRange(items);

            foreach (var item in items)
                IdMap[new ModelStoreKey(item.Id, null)] = item;
        }

        // Fire events outside lock
        if (!skipEvent)
        {
            var storeEvent = new ModelsSetEvent<TModel>();
            ModelsSet?.Invoke(storeEvent);
            Changed?.Invoke(storeEvent);
        }
    }

    public virtual void Clear(bool skipEvent = false)
    {
        lock (SyncLock)
        {
            List.Clear();
            IdMap.Clear();
        }

        // Fire events outside lock
        if (!skipEvent)
        {
            var storeEvent = new ModelsClearedEvent<TModel>();
            ModelsCleared?.Invoke(storeEvent);
            Changed?.Invoke(storeEvent);
        }
    }

    public bool TryGet(TId id, out TModel? item)
    {
        return TryGet(id, null, out item);
    }

    public bool TryGet(TId id, string? scope, out TModel? item)
    {
        lock (SyncLock)
        {
            return IdMap.TryGetValue(new ModelStoreKey(id, scope), out item);
        }
    }

    public TModel? Get(TId id)
    {
        return Get(id, null);
    }

    public TModel? Get(TId id, string? scope)
    {
        lock (SyncLock)
        {
            IdMap.TryGetValue(new ModelStoreKey(id, scope), out var item);
            return item;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TModel item)
    {
        lock (SyncLock)
        {
            return IdMap.ContainsKey(new ModelStoreKey(item.Id, null));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsId(TId id)
    {
        lock (SyncLock)
        {
            return IdMap.ContainsKey(new ModelStoreKey(id, null));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Sort(Comparison<TModel> comparison)
    {
        lock (SyncLock)
        {
            List.Sort(comparison);
        }
    }
    
    /// <summary>
    /// Exists so that external full list changes can be notified.
    /// </summary>
    public void NotifySet()
    {
        var storeEvent = new ModelsSetEvent<TModel>();
        ModelsSet?.Invoke(storeEvent);
        Changed?.Invoke(storeEvent);
    }
    
    public void Dispose()
    {
        Changed?.Dispose();

        ModelsSet?.Dispose();
        ModelsCleared?.Dispose();
        ModelsReordered?.Dispose();

        ModelAdded?.Dispose();
        ModelUpdated?.Dispose();
        ModelDeleted?.Dispose();

        Changed = null;

        lock (SyncLock)
        {
            List.Clear();
            IdMap.Clear();
        }
    }
}

/// <summary>
/// This version of the model store is ordered, and will automatically sort when items are added or updated.
/// </summary>
public class SortedModelStore<TModel, TId> : ModelStore<TModel, TId>
    where TModel : ClientModel<TModel, TId>, ISortable
    where TId : IEquatable<TId>
{
    public SortedModelStore(List<TModel>? startingList = null) : base(startingList)
    {
    }

    protected override ModelUpdatedEvent<TModel> HandleChanges(TModel existing, TModel updated)
    {
        var oldPos = existing.GetSortPosition();
        var newPos = updated.GetSortPosition();
        
        var baseResult = base.HandleChanges(existing, updated);
        
        if (oldPos != newPos)
        {
            baseResult.PositionChange = new PositionChange()
            {
                OldPosition = oldPos,
                NewPosition = newPos
            };
        }
        
        return baseResult;
    }

    protected override void InsertNew(TModel model, ModelInsertFlags flags)
    {
        if (flags.HasFlag(ModelInsertFlags.SkipSorting))
        {
            List.Add(model);
            return;
        }

        var index = List.BinarySearch(model, ISortable.Comparer);
        if (index < 0) index = ~index;

        List.Insert(index, model);
    }

    protected override IModelInsertionEvent<TModel>? PutInternal(TModel? model, ModelInsertFlags flags, string? scope)
    {
        // Always skip events in base - we'll fire them after repositioning
        var baseResult = base.PutInternal(model, flags | ModelInsertFlags.SkipEvents, scope);
        if (baseResult is null)
            return null;

        if (baseResult is ModelUpdatedEvent<TModel> updateEvent)
        {
            if (updateEvent.Changes is null && updateEvent.PositionChange is null)
            {
                // Nothing actually changed - skip events entirely
                return baseResult;
            }

            if (updateEvent.PositionChange is not null && !flags.HasFlag(ModelInsertFlags.SkipSorting))
            {
                var updated = updateEvent.GetModel();

                lock (SyncLock)
                {
                    var index = IndexOfReference(updated);
                    if (index >= 0)
                        List.RemoveAt(index);

                    var newIndex = List.BinarySearch(updated, ISortable.Comparer);
                    if (newIndex < 0) newIndex = ~newIndex;

                    List.Insert(newIndex, updated);
                }
            }
        }

        // Fire events outside lock
        if (!flags.HasFlag(ModelInsertFlags.SkipEvents))
        {
            FireEventsForResult(baseResult);
        }

        return baseResult;
    }

    private void FireEventsForResult(IModelInsertionEvent<TModel> result)
    {
        switch (result)
        {
            case ModelUpdatedEvent<TModel> updateEvent:
                updateEvent.GetModel().InvokeUpdatedEvent(updateEvent);
                ModelUpdated?.Invoke(updateEvent);
                break;
            case ModelAddedEvent<TModel> addEvent:
                ModelAdded?.Invoke(addEvent);
                break;
        }
        Changed?.Invoke(result);
    }

    public override void Set(List<TModel> items, bool skipEvent = false)
    {
        lock (SyncLock)
        {
            List.Clear();
            IdMap.Clear();

            List.AddRange(items);

            foreach (var item in items)
                IdMap[new ModelStoreKey(item.Id, null)] = item;

            List.Sort(ISortable.Compare);
        }

        // Fire events outside lock
        if (!skipEvent)
        {
            var setEvent = new ModelsSetEvent<TModel>();
            ModelsSet?.Invoke(setEvent);
            Changed?.Invoke(setEvent);
        }
    }

    public void Sort(bool skipEvent = false)
    {
        lock (SyncLock)
        {
            List.Sort(ISortable.Compare);
        }

        // Fire events outside lock
        if (!skipEvent)
        {
            var reorderEvent = new ModelsOrderedEvent<TModel>();
            ModelsReordered?.Invoke(reorderEvent);
            Changed?.Invoke(reorderEvent);
        }
    }
}
