using System.Collections;
using System.Numerics;
using System.Text.Json;
using Valour.Api.Client;
using Valour.Shared.Models;

namespace Valour.Api.Models;

public class PlanetModelObserver<T> : IEnumerable<T>, IDisposable where T : LiveModel, IPlanetModel
{
    /// <summary>
    /// If true, this collection will sort when necessary
    /// </summary>
    private readonly bool _sorted = false;

    /// <summary>
    /// The id of the planet this collection belongs to
    /// </summary>
    private readonly Planet _planet;
    
    /// <summary>
    /// The models within the collection
    /// </summary>
    private List<T> _models;
    
    /// <summary>
    /// False if this has never been initialized
    /// </summary>
    public bool Initialized => _models is not null;
    
    public PlanetModelObserver(Planet planet)
    {
        _planet = planet;
        
        // If the model is ordered, set the flag for sorting
        if (typeof(IOrderedModel).IsAssignableFrom(typeof(T)))
            _sorted = true;
    }
    
    #region IEnumerable
    
    /// <summary>
    /// Allow for indexing
    /// </summary>
    public T this[int index]  
    {  
        get => _models[index];  
        set => _models.Insert(index, value);
    }
    
    /// <summary>
    /// Allow enumeration
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        return _models.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
    
    #endregion
    
    public List<T> GetContents()
    {
        return _models;
    }
    

    /// <summary>
    /// Initializes the collection with the given models
    /// </summary>
    public async Task Initialize(List<T> newModels = null)
    {
        if (!Initialized)
        {
            // Subscribe to model events if this is the first time
            // we are initializing. This is to prevent hooking events
            // when we don't need to.
            ModelObserver<T>.OnAnyUpdated += OnModelUpdated;
            ModelObserver<T>.OnAnyDeleted += OnModelDeleted;
            
            _models = new List<T>();
        }
        else
        {
            _models.Clear();
        }
        
        // If there are no items, stop here.
        // We can't stop earlier because an event may add an item.
        if (newModels is null || newModels.Count == 0)
            return;
        
        
        // Retrieve cache values (this is necessary to ensure single copies of items)
        foreach (var model in newModels)
        {
            await model.AddToCache<T>(model, true);
            var cached = ValourCache.Get<T>(model.Id);
            if (cached is not null)
            {
                _models.Add(cached);
            }
        }
        
        Sort();
    }

    private Task OnModelUpdated(ModelUpdateEvent<T> e)
    {
        // Event is not for this planet
        if (e.Model.PlanetId != _planet.Id)
            return Task.CompletedTask;
        
        // Never initialized
        if (_models is null)
            return Task.CompletedTask;

        // If the item is new, add it to the collection
        // and then sort.
        if (e.NewToClient)
        {
            _models.Add(e.Model);
            Sort();
        }
        // If the item's position changed and we are sorting,
        // we need to sort.
        else if (_sorted)
        {
            if (e.PropsChanged.Contains(nameof(Channel.Position)))
                Sort();
        }

        return Task.CompletedTask;
    }

    private Task OnModelDeleted(T model)
    {
        // Event is not for this planet
        if (model.PlanetId != _planet.Id)
            return Task.CompletedTask;
        
        // Never initialized
        if (_models is null)
            return Task.CompletedTask;

        _models.Remove(model);
        
        return Task.CompletedTask;
    }

    public void Sort()
    {
        if (_sorted && _models is not null)
        {
            // TODO: This is a lot of casting. Can probably be optimized.
            _models.Sort((a, b) => ((IOrderedModel)a).Position.CompareTo(((IOrderedModel)b).Position));
        }
    }
    
    #region IDisposable
    
    /// <summary>
    /// For cleanup purposes
    /// </summary>
    private bool _disposed = false;
    
    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;
        
        if (disposing)
        {
            // Dispose managed resources.
            // Unsubscribe from model events
            ModelObserver<T>.OnAnyUpdated -= OnModelUpdated;
            ModelObserver<T>.OnAnyDeleted -= OnModelDeleted;
        }

        // Dispose unmanaged managed resources.

        _disposed = true;
    }

    void IDisposable.Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    ~PlanetModelObserver() {
        Dispose(false);
    }
    
    #endregion
}