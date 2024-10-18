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

    public void Upsert(TModel item)
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
        
        if (ItemChange is not null)
            ItemChange.Invoke(new ListChangeEvent<TModel>(changeType, item));
    }
    
    public void Remove(TModel item)
    {
        if (!List.Contains(item))
            return;
        
        List.Remove(item);
        IdMap.Remove(item.Id);
        
        if (ItemChange is not null)
            ItemChange.Invoke(new ListChangeEvent<TModel>(ListItemChangeType.Remove, item));
    }
    
    public void Set(List<TModel> items)
    {
        List = items;
        Values = List;
        IdMap = List.ToDictionary(x => x.Id);
        if (ListChange is not null)
            ListChange.Invoke(ListFullChangeType.Set);
    }
    
    public void Clear()
    {
        List.Clear();
        IdMap.Clear();
        if (ListChange is not null)
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
    
    public new void Upsert(TModel item)
    {
        ListItemChangeType changeType;
        
        if (!List.Contains(item))
        {
            List.Add(item);
            IdMap[item.Id] = item;
            Sort();
            changeType = ListItemChangeType.Add;
        }
        else
        {
            Sort();
            changeType = ListItemChangeType.Update;
        }
        
        if (ItemChange is not null)
            ItemChange.Invoke(new ListChangeEvent<TModel>(changeType, item));
    }

    public void UpsertNoSort(TModel item)
    {
        base.Upsert(item);
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