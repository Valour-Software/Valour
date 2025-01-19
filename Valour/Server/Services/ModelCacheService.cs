using System.Collections.Concurrent;

namespace Valour.Server.Services;

public class ModelCacheService
{
    public readonly ModelCache<HostedPlanet, long> HostedPlanets = new();
}

public class ModelCache<TModel, TId> where TModel: ServerModel<TId>
{
    private readonly ConcurrentDictionary<TId, TModel> _cache = new();
    
    public TModel Get(TId id)
    {
        _cache.TryGetValue(id, out var model);
        return model;
    }
    
    public void Set(TModel model)
    {
        _cache[model.Id] = model;
    }
    
    public void Remove(TId id)
    {
        _cache.TryRemove(id, out _);
    }
    
    public void Remove(TModel model)
    {
        _cache.TryRemove(model.Id, out _);
    }
    
    public void SetRange(IEnumerable<TModel> models)
    {
        foreach (var model in models)
        {
            _cache[model.Id] = model;
        }
    }
    
    public bool ContainsKey(TId id)
    {
        return _cache.ContainsKey(id);
    }
    
    public bool ContainsValue(TModel model)
    {
        return _cache.ContainsKey(model.Id);
    }
    
    public TId[] Ids => _cache.Keys.ToArray();
    
    public void Clear()
    {
        _cache.Clear();
    }
}