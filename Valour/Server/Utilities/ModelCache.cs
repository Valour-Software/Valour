using System.Collections;
using Valour.Shared.Extensions;
using Valour.Shared.Models;

namespace Valour.Server.Utilities;

/// <summary>
/// The ModelCache is used for
/// caching collections of models that are frequently accessed and updated.
/// It performs no allocations and protects the internal store.
/// </summary>
public class ModelCache<T, TId> where T : ISharedModel<TId>
{
    public IReadOnlyList<T> Values { get; private set; }
    public IReadOnlyDictionary<TId, T> Lookup { get; private set; }
    
    private List<T> _cache;
    private Dictionary<TId, T> _lookup;
    
    public ModelCache()
    {
        _cache = new();
        _lookup = new();
    }
    
    public ModelCache(IEnumerable<T> initial)
    {
        _cache = initial.ToList();
        _lookup = _cache.ToDictionary(x => x.Id);
    }
    
    public void Reset(IEnumerable<T> initial)
    {
        _cache = initial.ToList();
        _lookup = _cache.ToDictionary(x => x.Id);
    }
    
    public void Add(T item)
    {
        _cache.Add(item);
        _lookup.Add(item.Id, item);
    }
    
    public void Remove(TId id)
    {
        if (_lookup.TryGetValue(id, out var item))
        {
            _cache.Remove(item);
            _lookup.Remove(id);
        }
    }
    
    public void Update(T updated)
    {
        if (_lookup.TryGetValue(updated.Id, out var old))
        {
            updated.CopyAllTo(old);
        }
        else
        {
            Add(updated);
        }
    }
    
    public T Get(TId id)
    {
        _lookup.TryGetValue(id, out var item);
        return item;
    }
}

public class SortedModelCache<T, TId> where T : ISortableModel, ISharedModel<TId>
{
    public IReadOnlyList<T> Values { get; private set; }
    public IReadOnlyDictionary<TId, T> Lookup { get; private set; }
    
    private List<T> _cache;
    private Dictionary<TId, T> _lookup;
    
    public SortedModelCache()
    {
        _cache = new();
        _lookup = new();
    }
    
    public SortedModelCache(IEnumerable<T> initial)
    {
        _cache = initial.ToList();
        _lookup = _cache.ToDictionary(x => x.Id);
        
        if (_cache.Count > 0)
        {
            _cache.Sort(ISortableModel.Compare);
        }
    }
    
    public void Reset(IEnumerable<T> initial)
    {
        _cache = initial.ToList();
        _lookup = _cache.ToDictionary(x => x.Id);
        
        if (_cache.Count > 0)
        {
            _cache.Sort(ISortableModel.Compare);
        }
    }
    
    public void Add(T item)
    {
        _cache.Add(item);
        _lookup.Add(item.Id, item);
        _cache.Sort(ISortableModel.Compare);
    }
    
    public void Remove(TId id)
    {
        if (_lookup.TryGetValue(id, out var item))
        {
            _cache.Remove(item);
            _lookup.Remove(id);
        }
    }
    
    public void Update(T updated)
    {
        if (_lookup.TryGetValue(updated.Id, out var old))
        {
            var oldPos = old.GetSortPosition();
            updated.CopyAllTo(old);
            
            // check if the position has changed
            if (oldPos != updated.GetSortPosition())
            {
                _cache.Sort(ISortableModel.Compare);
            }
        }
        else
        {
            Add(updated);
            _cache.Sort(ISortableModel.Compare);
        }
    }
    
    public T Get(TId id)
    {
        _lookup.TryGetValue(id, out var item);
        return item;
    }
}