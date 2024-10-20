using System.Runtime.CompilerServices;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.ModelLogic;

public enum ListItemChangeType
{
    Add,
    Update,
    Remove,
}

public enum ListFullChangeType
{
    Set,
    Clear
}

public readonly struct ListChangeEvent<T>
{
    public readonly ListItemChangeType ChangeType;
    public readonly T Item;
    
    public ListChangeEvent(ListItemChangeType changeType, T item)
    {
        ChangeType = changeType;
        Item = item;
    }
}

/// <summary>
/// Reactive lists are lists that can be observed for changes.
/// </summary>
public class ReactiveModelStore<TModel, TId>
    where TModel : ClientModel<TModel, TId>
    where TId : IEquatable<TId>
{
    public HybridEvent<ListChangeEvent<TModel>> ItemChange; // We don't assign because += and -= will do it
    public HybridEvent<ListFullChangeType> ListChange;
    
    protected List<TModel> List;
    protected Dictionary<TId, TModel> IdMap;
    public IReadOnlyList<TModel> Values;
    
    public ReactiveModelStore(List<TModel> startingList = null)
    {
        List = startingList ?? new List<TModel>();
        IdMap = List.ToDictionary(x => x.Id);
        Values = List;
    }

    public virtual void Upsert(TModel item, bool skipEvent = false)
    {  
        ListItemChangeType changeType;
        
        if (!List.Contains(item))
        {
            List.Add(item);
            changeType = ListItemChangeType.Add;
        }
        else
        {
            changeType = ListItemChangeType.Update;
        }
        
        IdMap[item.Id] = item;
        
        if (!skipEvent && ItemChange is not null)
            ItemChange.Invoke(new ListChangeEvent<TModel>(changeType, item));
    }
    
    public virtual void Remove(TModel item, bool skipEvent = false)
    {
        if (!IdMap.ContainsKey(item.Id))
            return;
        
        List.Remove(item);
        IdMap.Remove(item.Id);
        
        if (!skipEvent && ItemChange is not null)
            ItemChange.Invoke(new ListChangeEvent<TModel>(ListItemChangeType.Remove, item));
    }
    
    public virtual void Set(List<TModel> items, bool skipEvent = false)
    {
        // We clear rather than replace the list to ensure that the reference is maintained
        // Because the reference may be used across the application.
        List.Clear();
        IdMap.Clear();
        
        List.AddRange(items);
        IdMap = List.ToDictionary(x => x.Id);
        if (!skipEvent && ListChange is not null)
            ListChange.Invoke(ListFullChangeType.Set);
    }
    
    public virtual void Clear(bool skipEvent = false)
    {
        List.Clear();
        IdMap.Clear();
        if (!skipEvent && ListChange is not null)
            ListChange.Invoke(ListFullChangeType.Clear);
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
        if (ListChange is not null)
            ListChange.Invoke(ListFullChangeType.Set);
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

    public void Upsert(TModel item, bool skipEvent = false, PositionChange? positionChange = null)
    {
        ListItemChangeType changeType;

        var index = List.BinarySearch(item, ISortable.Comparer);
        if (index < 0)
        {
            // Insert new item at the correct position
            index = ~index;
            List.Insert(index, item);
            IdMap[item.Id] = item;
            changeType = ListItemChangeType.Add;
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

            changeType = ListItemChangeType.Update;
        }

        if (!skipEvent && ItemChange is not null)
            ItemChange?.Invoke(new ListChangeEvent<TModel>(changeType, item));
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
        
        if (!skipEvent && ListChange is not null)
            ListChange.Invoke(ListFullChangeType.Set);
    }

    public void Sort()
    {
        List.Sort(ISortable.Compare);
    }
}

public static class ReactiveListExtensions
{
    public static ReactiveModelStore<TModel, TId> ClearOrInit<TModel, TId>(this ReactiveModelStore<TModel,TId> modelStore)
        where TModel : ClientModel<TModel, TId>
        where TId : IEquatable<TId>
    {
        if (modelStore is null)
            return new ReactiveModelStore<TModel, TId>();
        
        modelStore.Clear();
        return modelStore;
    }
    
    public static SortedReactiveModelStore<TModel, TId> ClearOrInit<TModel, TId>(this SortedReactiveModelStore<TModel, TId> modelStore)
        where TModel : ClientModel<TModel, TId>, ISortable
        where TId : IEquatable<TId>
    {
        if (modelStore is null)
            return new SortedReactiveModelStore<TModel, TId>();
        
        modelStore.Clear();
        return modelStore;
    }
}