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
    
    private List<T> _list = new();
    
    public async Task AddOrNotifyUpdate(T item)
    {
        ListItemChangeType changeType;
        
        if (!_list.Contains(item))
        {
            _list.Add(item);
            changeType = ListItemChangeType.Add;
        }
        else
        {
            changeType = ListItemChangeType.Update;
        }
        
        if (ItemChange is not null)
            await ItemChange.Invoke(new ListChangeEvent<T>(changeType, item));
    }
    
    public async Task Remove(T item)
    {
        if (!_list.Contains(item))
            return;
        
        _list.Remove(item);
        
        if (ItemChange is not null)
            await ItemChange.Invoke(new ListChangeEvent<T>(ListItemChangeType.Remove, item));
    }
    
    public async Task Set(List<T> items)
    {
        _list = items;
        if (ListChange is not null)
            await ListChange.Invoke(ListFullChangeType.Set);
    }
    
    public async Task Clear()
    {
        _list.Clear();
        if (ListChange is not null)
            await ListChange.Invoke(ListFullChangeType.Clear);
    }
}