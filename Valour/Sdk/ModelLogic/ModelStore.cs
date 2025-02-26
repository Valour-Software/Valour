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
    
    public int Count => List.Count;
    
    public ModelStore(List<TModel>? startingList = null)
    {
        List = startingList ?? new List<TModel>();
        IdMap = List.ToDictionary(x => x.Id);
    }
    
    // Make iterable
    public TModel this[int index] => List[index];
    public IEnumerator<TModel> GetEnumerator() => List.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => List.GetEnumerator();

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
        
        if (IdMap.TryGetValue(model.Id, out var existing))
        {
            // Add change markers to event data and apply changes
            var modelEventData = HandleChanges(existing, model);
            
            // Check if nothing changed
            if (modelEventData.Changes is null)
            {
                return modelEventData;
            }

            if (!flags.HasFlag(ModelInsertFlags.SkipEvents))
            {
                existing.InvokeUpdatedEvent(modelEventData);
                ModelUpdated?.Invoke(modelEventData);
                Changed?.Invoke(modelEventData);
            }
            
            result = modelEventData;
        }
        else
        {
            List.Add(model);
            IdMap[model.Id] = model;
            
            var addedEvent = new ModelAddedEvent<TModel>(model);
            ModelAdded?.Invoke(addedEvent);
            Changed?.Invoke(addedEvent);
            
            result = addedEvent;
        }

        return result;
    }
    
    public TModel? Remove(TModel item, bool skipEvent = false)
    {
        return Remove(item.Id, skipEvent);
    }
    
    public TModel? Remove(TId id, bool skipEvent = false)
    {
        if (!IdMap.ContainsKey(id))
            return null;
        
        IdMap.Remove(id);
        
        // Get index of item in list
        var index = List.FindIndex(x => x.Id.Equals(id));
        var item = List[index];
        List.RemoveAt(index);
        
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
        // We clear rather than replace the list to ensure that the reference is maintained
        // Because the reference may be used across the application.
        List.Clear();
        IdMap.Clear();
        
        List.AddRange(items);
        
        foreach (var item in items)
            IdMap[item.Id] = item;

        if (!skipEvent)
        {
            var storeEvent = new ModelsSetEvent<TModel>();
            ModelsSet?.Invoke(storeEvent);
            Changed?.Invoke(storeEvent);
        }
    }
    
    public virtual void Clear(bool skipEvent = false)
    {
        List.Clear();
        IdMap.Clear();
        if (!skipEvent)
        {
            var storeEvent = new ModelsClearedEvent<TModel>();
            ModelsCleared?.Invoke(storeEvent);
            Changed?.Invoke(storeEvent);
        }
    }
    
    public bool TryGet(TId id, out TModel? item)
    {
        return IdMap.TryGetValue(id, out item);
    }
    
    public TModel? Get(TId id)
    {
        IdMap.TryGetValue(id, out var item);
        return item;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TModel item)
    {
        return IdMap.ContainsKey(item.Id); // This is faster than List.Contains
        // return List.Contains(item);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsId(TId id)
    {
        return IdMap.ContainsKey(id);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Sort(Comparison<TModel> comparison)
    {
        List.Sort(comparison);
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
        
        List.Clear();
        IdMap.Clear();
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
        var baseResult = base.PutInternal(model, flags); // Do base put without event
        if (baseResult is null)
            return null;
        
        // Don't bother positioning the item properly if we're skipping sorting
        if (flags.HasFlag(ModelInsertFlags.SkipSorting))
            return baseResult;
        
        bool doRemoval = false;

        ModelUpdatedEvent<TModel>? updateEvent = null;
        ModelAddedEvent<TModel>? addEvent = null;

        switch (baseResult)
        {
            case ModelUpdatedEvent<TModel> update:
                updateEvent = update;
                if (updateEvent.PositionChange is null)
                    return baseResult; // No need to sort if no position change
                else
                    doRemoval = true; // We need to remove the existing item before re-inserting it
                break;
            case ModelAddedEvent<TModel> add:
                addEvent = add;
                doRemoval = true;
                break;
        }
        
        var resultModel = baseResult.GetModel();

        if (doRemoval)
        {
            // Get the index of the item
            var index = List.FindIndex(x => x.Id.Equals(resultModel.Id));
            // Remove the item from the list
            List.RemoveAt(index);
        }

        // Find the new index
        var newIndex = List.BinarySearch(resultModel, ISortable.Comparer);
        if (newIndex < 0) newIndex = ~newIndex;

        List.Insert(newIndex, resultModel);
        
        if (!flags.HasFlag(ModelInsertFlags.SkipEvents))
        {
            if (updateEvent is not null) 
                ModelUpdated?.Invoke(updateEvent);
            else if (addEvent is not null)
                ModelAdded?.Invoke(addEvent.Value);
            
            Changed?.Invoke(baseResult);
        }

        return baseResult;
    }

    public override void Set(List<TModel> items, bool skipEvent = false)
    {
        List.Clear();
        IdMap.Clear();
        
        List.AddRange(items);
        
        foreach (var item in items)
            IdMap[item.Id] = item;
        
        Sort(true);

        if (!skipEvent)
        {
            var setEvent = new ModelsSetEvent<TModel>();
            ModelsSet?.Invoke(setEvent);
            Changed?.Invoke(setEvent);
        }
    }

    public void Sort(bool skipEvent = false)
    {
        List.Sort(ISortable.Compare);

        if (!skipEvent)
        {
            var reorderEvent = new ModelsOrderedEvent<TModel>();
            ModelsReordered?.Invoke(reorderEvent);
            Changed?.Invoke(reorderEvent);
        }
    }
}