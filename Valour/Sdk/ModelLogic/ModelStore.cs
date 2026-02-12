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
    protected readonly Dictionary<TId, TModel> IdMap;

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
        IdMap = List.ToDictionary(x => x.Id);
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

    private bool ValuesDiffer(object? a, object? b)
    {
        if (a is null)
            return b is not null;
        return b is null || !a.Equals(b);
    }
    
    protected virtual ModelUpdatedEvent<TModel> HandleChanges(TModel existing, TModel updated)
    {
        Dictionary<string, object> changes = null;
        
        var type = existing.GetType();
        var properties = ModelUpdateUtils.ModelPropertyCache[type];
        var getters = ModelUpdateUtils.ModelGetterCache[type];
        var setters = ModelUpdateUtils.ModelSetterCache[type];
    
        for (var i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            var a = getters[i](existing);
            var b = getters[i](updated);

            if (ValuesDiffer(a, b))
            {
                if (changes is null)
                    changes = ModelUpdateUtils.ChangeDictPool.Get();
                
                var factory = ChangeFactoryCache.GetOrAddFactory(prop.PropertyType);
                var change = factory(a, b);
                changes[prop.Name] = change;
            }

            // Apply the change to the existing model
            setters[i](existing, b);
        }
        
        return changes is null ? 
            new ModelUpdatedEvent<TModel>(existing, null) :
            new ModelUpdatedEvent<TModel>(existing, new ModelChange<TModel>(changes));
    }
    
    public TModel? Put(TModel model, ModelInsertFlags flags = ModelInsertFlags.None)
    {
        var result = PutInternal(model, flags);
        return result?.GetModel();
    }
    
    protected virtual IModelInsertionEvent<TModel>? PutInternal(TModel? model, ModelInsertFlags flags)
    {
        if (model is null)
            return null;

        IModelInsertionEvent<TModel>? result;
        bool isUpdate;
        TModel existing;

        lock (SyncLock)
        {
            if (IdMap.TryGetValue(model.Id, out existing))
            {
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
                List.Add(model);
                IdMap[model.Id] = model;
                result = new ModelAddedEvent<TModel>(model);
            }
        }

        // Fire events outside lock to prevent deadlocks
        if (!flags.HasFlag(ModelInsertFlags.SkipEvents))
        {
            if (isUpdate)
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
        return Remove(item.Id, skipEvent);
    }
    
    public TModel? Remove(TId id, bool skipEvent = false)
    {
        TModel item;

        lock (SyncLock)
        {
            if (!IdMap.TryGetValue(id, out item))
                return null;

            IdMap.Remove(id);

            // Get index of item in list
            var index = List.FindIndex(x => x.Id.Equals(id));
            List.RemoveAt(index);
        }

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
                IdMap[item.Id] = item;
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
        lock (SyncLock)
        {
            return IdMap.TryGetValue(id, out item);
        }
    }

    public TModel? Get(TId id)
    {
        lock (SyncLock)
        {
            IdMap.TryGetValue(id, out var item);
            return item;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TModel item)
    {
        lock (SyncLock)
        {
            return IdMap.ContainsKey(item.Id);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsId(TId id)
    {
        lock (SyncLock)
        {
            return IdMap.ContainsKey(id);
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

    protected override IModelInsertionEvent<TModel>? PutInternal(TModel? model, ModelInsertFlags flags)
    {
        // Always skip events in base - we'll fire them after repositioning
        var baseFlags = flags | ModelInsertFlags.SkipEvents;
        var baseResult = base.PutInternal(model, baseFlags);
        if (baseResult is null)
            return null;

        // Don't bother positioning the item properly if we're skipping sorting
        if (flags.HasFlag(ModelInsertFlags.SkipSorting))
        {
            // Still need to fire events if not skipping
            if (!flags.HasFlag(ModelInsertFlags.SkipEvents))
            {
                FireEventsForResult(baseResult);
            }
            return baseResult;
        }

        bool doRemoval = false;
        ModelUpdatedEvent<TModel>? updateEvent = null;
        ModelAddedEvent<TModel>? addEvent = null;

        switch (baseResult)
        {
            case ModelUpdatedEvent<TModel> update:
                updateEvent = update;
                if (updateEvent.Changes is null && updateEvent.PositionChange is null)
                {
                    // Nothing actually changed - skip events entirely
                    return baseResult;
                }
                if (updateEvent.PositionChange is null)
                {
                    // No position change - just fire events and return
                    if (!flags.HasFlag(ModelInsertFlags.SkipEvents))
                    {
                        FireEventsForResult(baseResult);
                    }
                    return baseResult;
                }
                doRemoval = true;
                break;
            case ModelAddedEvent<TModel> add:
                addEvent = add;
                doRemoval = true;
                break;
        }

        var resultModel = baseResult.GetModel();

        // Reposition within lock
        lock (SyncLock)
        {
            if (doRemoval)
            {
                var index = List.FindIndex(x => x.Id.Equals(resultModel.Id));
                List.RemoveAt(index);
            }

            var newIndex = List.BinarySearch(resultModel, ISortable.Comparer);
            if (newIndex < 0) newIndex = ~newIndex;

            List.Insert(newIndex, resultModel);
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
                IdMap[item.Id] = item;

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