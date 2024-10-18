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
public class ReactiveList<T>
{
    public HybridEvent<ListChangeEvent<T>> ItemChange; // We don't assign because += and -= will do it
    public HybridEvent<ListFullChangeType> ListChange;
    
    protected List<T> List;
    public IReadOnlyList<T> Values;
    
    public ReactiveList(List<T> startingList = null)
    {
        List = startingList ?? new List<T>();
        Values = List;
    }

    public void Upsert(T item)
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
        
        if (ItemChange is not null)
            ItemChange.Invoke(new ListChangeEvent<T>(changeType, item));
    }
    
    public void Remove(T item)
    {
        if (!List.Contains(item))
            return;
        
        List.Remove(item);
        
        if (ItemChange is not null)
            ItemChange.Invoke(new ListChangeEvent<T>(ListItemChangeType.Remove, item));
    }
    
    public void Set(List<T> items)
    {
        List = items;
        Values = List;
        if (ListChange is not null)
            ListChange.Invoke(ListFullChangeType.Set);
    }
    
    public void Clear()
    {
        List.Clear();
        if (ListChange is not null)
            ListChange.Invoke(ListFullChangeType.Clear);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(T item)
    {
        return List.Contains(item);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Sort(Comparison<T> comparison)
    {
        List.Sort(comparison);
    }
}

/// <summary>
/// Reactive lists are lists that can be observed for changes.
/// This version of the list is ordered, and will automatically sort the list when items are added or updated.
/// </summary>
public class SortedReactiveList<T> : ReactiveList<T>
    where T : ISortable
{
    public SortedReactiveList(List<T> startingList = null) : base(startingList)
    {
    }    
    
    public new void Upsert(T item)
    {
        ListItemChangeType changeType;
        
        if (!List.Contains(item))
        {
            List.Add(item);
            Sort();
            changeType = ListItemChangeType.Add;
        }
        else
        {
            Sort();
            changeType = ListItemChangeType.Update;
        }
        
        if (ItemChange is not null)
            ItemChange.Invoke(new ListChangeEvent<T>(changeType, item));
    }

    public void UpsertNoSort(T item)
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
    public static ReactiveList<T> ClearOrInit<T>(this ReactiveList<T> list)
    {
        if (list is null)
            return new ReactiveList<T>();
        
        list.Clear();
        return list;
    }
    
    public static SortedReactiveList<T> ClearOrInit<T>(this SortedReactiveList<T> list)
        where T : ISortable
    {
        if (list is null)
            return new SortedReactiveList<T>();
        
        list.Clear();
        return list;
    }
}