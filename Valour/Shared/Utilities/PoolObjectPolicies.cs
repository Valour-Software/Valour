using Microsoft.Extensions.ObjectPool;
using System.Collections.Generic;


namespace Valour.Shared.Utilities;

public class ListPooledObjectPolicy<T> : PooledObjectPolicy<List<T>>
{
    public override List<T> Create()
    {
        return new List<T>();
    }

    public override bool Return(List<T> obj)
    {
        // Clear the list so it’s fresh for the next user
        obj.Clear();
        // Return true to indicate the object can be reused
        return true;
    }
}

public class DictionaryPooledObjectPolicy<TKey, TValue> : PooledObjectPolicy<Dictionary<TKey, TValue>>
{
    public override Dictionary<TKey, TValue> Create()
    {
        return new Dictionary<TKey, TValue>();
    }

    public override bool Return(Dictionary<TKey, TValue> obj)
    {
        // Clear the dictionary
        obj.Clear();
        return true;
    }
}