using System.Collections;
using System.Runtime.CompilerServices;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.ModelLogic;

public enum ListChangeType
{
    Added,
    Updated,
    Removed,
    Set,
    Cleared,
    Reordered
}

public readonly struct ModelListChangeEvent<T>
    where T : class
{
    public static readonly ModelListChangeEvent<T> Set = new(ListChangeType.Set, default);
    public static readonly ModelListChangeEvent<T> Clear = new(ListChangeType.Cleared, default);
    public static readonly ModelListChangeEvent<T> Reordered = new(ListChangeType.Reordered, default);
    
    public readonly ListChangeType ChangeType;
    public readonly T Model;
    
    public ModelListChangeEvent(ListChangeType changeType, T model)
    {
        ChangeType = changeType;
        Model = model;
    }
}

/// <summary>
/// Reactive lists are lists that can be observed for changes.
/// </summary>
public class ModelList<TModel, TId> : IEnumerable<TModel>, IDisposable
    where TModel : ClientModel<TModel, TId>
    where TId : IEquatable<TId>
{
    public HybridEvent<ModelListChangeEvent<TModel>> Changed; // We don't assign because += and -= will do it
    
    protected readonly List<TModel> List;
    protected Dictionary<TId, TModel> IdMap;
    public IReadOnlyList<TModel> Values;
    
    public int Count => List.Count;
    
    public ModelList(List<TModel> startingList = null)
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
            changeType = ListChangeType.Added;
        }
        else
        {
            changeType = ListChangeType.Updated;
        }
        
        IdMap[item.Id] = item;
        
        if (!skipEvent && Changed is not null)
            Changed.Invoke(new ModelListChangeEvent<TModel>(changeType, item));
    }
    
    public virtual void Remove(TModel item, bool skipEvent = false)
    {
        if (!IdMap.ContainsKey(item.Id))
            return;
        
        List.Remove(item);
        IdMap.Remove(item.Id);
        
        if (!skipEvent && Changed is not null)
            Changed.Invoke(new ModelListChangeEvent<TModel>(ListChangeType.Removed, item));
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
            Changed.Invoke(ModelListChangeEvent<TModel>.Set);
    }
    
    public virtual void Clear(bool skipEvent = false)
    {
        List.Clear();
        IdMap.Clear();
        if (!skipEvent && Changed is not null)
            Changed.Invoke(ModelListChangeEvent<TModel>.Clear);
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
        Changed?.Invoke(ModelListChangeEvent<TModel>.Set);
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
public class SortedModelList<TModel, TId> : ModelList<TModel, TId>
    where TModel : ClientModel<TModel, TId>, ISortable
    where TId : IEquatable<TId>
{
    public SortedModelList(List<TModel> startingList = null) : base(startingList)
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
            changeType = ListChangeType.Added;
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

            changeType = ListChangeType.Updated;
        }

        if (!skipEvent && Changed is not null)
            Changed?.Invoke(new ModelListChangeEvent<TModel>(changeType, item));
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
        
        Sort(false);
        
        if (!skipEvent && Changed is not null)
            Changed.Invoke(ModelListChangeEvent<TModel>.Set);
    }

    public void Sort(bool skipEvent = false)
    {
        List.Sort(ISortable.Compare);
        
        if (!skipEvent && Changed is not null)
            Changed.Invoke(ModelListChangeEvent<TModel>.Reordered);
    }
}