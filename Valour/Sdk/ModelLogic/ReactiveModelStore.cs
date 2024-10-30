using System.Collections;
using System.Runtime.CompilerServices;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.ModelLogic;

public enum ListChangeType
{
    Add,
    Update,
    Remove,
    Set,
    Clear
}

public readonly struct ModelStoreEvent<T>
    where T : class
{
    public static readonly ModelStoreEvent<T> Set = new(ListChangeType.Set, default);
    public static readonly ModelStoreEvent<T> Clear = new(ListChangeType.Clear, default);
    
    public readonly ListChangeType ChangeType;
    public readonly T Item;
    
    public ModelStoreEvent(ListChangeType changeType, T item)
    {
        ChangeType = changeType;
        Item = item;
    }
}

/// <summary>
/// Reactive lists are lists that can be observed for changes.
/// </summary>
public class ReactiveModelStore<TModel, TId> : IEnumerable<TModel>, IDisposable
    where TModel : ClientModel<TModel, TId>
    where TId : IEquatable<TId>
{
    public HybridEvent<ModelStoreEvent<TModel>> Changed; // We don't assign because += and -= will do it
    
    protected readonly List<TModel> List;
    protected Dictionary<TId, TModel> IdMap;
    public IReadOnlyList<TModel> Values;
    
    public ReactiveModelStore(List<TModel> startingList = null)
    {
        List = startingList ?? new List<TModel>();
        IdMap = List.ToDictionary(x => x.Id);
        Values = List;
    }
    
    // Make iterable
    public TModel this[int index] => List[index];
    public IEnumerator<TModel> GetEnumerator() => List.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => List.GetEnumerator();

    public virtual void Upsert(TModel item, bool skipEvent = false)
    {  
        ListChangeType changeType;
        
        if (!List.Contains(item))
        {
            List.Add(item);
            changeType = ListChangeType.Add;
        }
        else
        {
            changeType = ListChangeType.Update;
        }
        
        IdMap[item.Id] = item;
        
        if (!skipEvent && Changed is not null)
            Changed.Invoke(new ModelStoreEvent<TModel>(changeType, item));
    }
    
    public virtual void Remove(TModel item, bool skipEvent = false)
    {
        if (!IdMap.ContainsKey(item.Id))
            return;
        
        List.Remove(item);
        IdMap.Remove(item.Id);
        
        if (!skipEvent && Changed is not null)
            Changed.Invoke(new ModelStoreEvent<TModel>(ListChangeType.Remove, item));
    }
    
    public virtual void Set(List<TModel> items, bool skipEvent = false)
    {
        // We clear rather than replace the list to ensure that the reference is maintained
        // Because the reference may be used across the application.
        List.Clear();
        IdMap.Clear();
        
        List.AddRange(items);
        IdMap = List.ToDictionary(x => x.Id);
        
        if (!skipEvent && Changed is not null)
            Changed.Invoke(ModelStoreEvent<TModel>.Set);
    }
    
    public virtual void Clear(bool skipEvent = false)
    {
        List.Clear();
        IdMap.Clear();
        if (!skipEvent && Changed is not null)
            Changed.Invoke(ModelStoreEvent<TModel>.Clear);
    }
    
    public bool TryGet(TId id, out TModel item)
    {
        return IdMap.TryGetValue(id, out item);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TModel item)
    {
        return IdMap.ContainsKey(item.Id); // This is faster than List.Contains
        // return List.Contains(item);
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
        Changed?.Invoke(ModelStoreEvent<TModel>.Set);
    }
    
    public void Dispose()
    {
        Changed?.Dispose();
        
        Changed = null;
        
        List.Clear();
        IdMap.Clear();
    }
}

/// <summary>
/// Reactive lists are lists that can be observed for changes.
/// This version of the list is ordered, and will automatically sort the list when items are added or updated.
/// </summary>
public class SortedReactiveModelStore<TModel, TId> : ReactiveModelStore<TModel, TId>
    where TModel : ClientModel<TModel, TId>, ISortable
    where TId : IEquatable<TId>
{
    public SortedReactiveModelStore(List<TModel> startingList = null) : base(startingList)
    {
    }

    public void Upsert(ModelUpdateEvent<TModel> updateEvent, bool skipEvent = false)
    {
        Upsert(updateEvent.Model, skipEvent, updateEvent.PositionChange);
    }

    public override void Upsert(TModel item, bool skipEvent = false)
    {
        Upsert(item, skipEvent, null);
    }

    /// <summary>
    /// Updates if the item is in the list. If the item is not in the list, it is ignored.
    /// </summary>
    public void Update(ModelUpdateEvent<TModel> updateEvent, bool skipEvent = false)
    {
        if (!Contains(updateEvent.Model))
            return;
        
        Upsert(updateEvent.Model, skipEvent, updateEvent.PositionChange);
    }

    public void Upsert(TModel item, bool skipEvent = false, PositionChange? positionChange = null)
    {
        ListChangeType changeType;

        var index = List.BinarySearch(item, ISortable.Comparer);
        if (index < 0)
        {
            // Insert new item at the correct position
            index = ~index;
            List.Insert(index, item);
            IdMap[item.Id] = item;
            changeType = ListChangeType.Add;
        }
        else
        {
            // If positionChange is specified, resort the item
            if (positionChange is not null)
            {
                List.RemoveAt(index);
                var newIndex = List.BinarySearch(item, ISortable.Comparer);
                if (newIndex < 0) newIndex = ~newIndex;
                List.Insert(newIndex, item);
            }

            changeType = ListChangeType.Update;
        }

        if (!skipEvent && Changed is not null)
            Changed?.Invoke(new ModelStoreEvent<TModel>(changeType, item));
    }


    public void UpsertNoSort(TModel item, bool skipEvent = false)
    {
        base.Upsert(item, skipEvent);
    }

    public override void Set(List<TModel> items, bool skipEvent = false)
    {
        List.Clear();
        IdMap.Clear();
        
        List.AddRange(items);
        IdMap = List.ToDictionary(x => x.Id);
        
        Sort();
        
        if (!skipEvent && Changed is not null)
            Changed.Invoke(ModelStoreEvent<TModel>.Set);
    }

    public void Sort()
    {
        List.Sort(ISortable.Compare);
    }
}