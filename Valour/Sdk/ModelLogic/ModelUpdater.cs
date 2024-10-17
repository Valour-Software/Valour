﻿using System.Reflection;
using Microsoft.Extensions.ObjectPool;
using Valour.Shared.Extensions;

namespace Valour.Sdk.ModelLogic;

/// <summary>
/// Responsible for pushing model updates across the client
/// </summary>
public static class ModelUpdater
{
    
    // Cache for type properties
    // The properties of models are cached to avoid reflection overhead
    // It's not like the properties will change during runtime
    private static readonly Dictionary<Type, PropertyInfo[]> ModelPropertyCache = new ();
    
    // Same for fields
    private static readonly Dictionary<Type, FieldInfo[]> ModelFieldCache = new ();
    
    // Pool for PropsChanged hashsets
    private static readonly ObjectPool<HashSet<string>> HashSetPool = 
        new DefaultObjectPoolProvider().Create(new HashSetPooledObjectPolicy<string>());

    static ModelUpdater()
    {
        // Cache all properties of all models
        foreach (var modelType in typeof(ClientModel).Assembly.GetTypes())
        {
            if (modelType.IsSubclassOf(typeof(ClientModel)))
            {
                ModelPropertyCache[modelType] = modelType.GetProperties();
                ModelFieldCache[modelType] = modelType.GetFields();
            }
        }
    }
    
    public static void ReturnPropsChanged(HashSet<string> propsChanged)
    {
        HashSetPool.Return(propsChanged);
    }
    
    /// <summary>
    /// Updates a model's properties and returns the global instance
    /// </summary>
    public static async Task<TModel> UpdateItemAsync<TModel, TId>(TModel updated, TModel cached, int flags, bool skipEvent = false) 
        where TModel : ClientModel<TModel, TId>
        where TId : IEquatable<TId>
    {
        // Create object for event data
        var eventData = new ModelUpdateEvent<TModel>()
        {
            Flags = flags,
            PropsChanged = HashSetPool.Get(),
            Model = cached,
            NewToClient = cached is null
        };
        
        if (!eventData.NewToClient)
        {
            // Find changed properties
            var pInfo = ModelPropertyCache[typeof(TModel)];

            foreach (var prop in pInfo)
            {
                if (prop.GetValue(cached) != prop.GetValue(updated))
                    eventData.PropsChanged.Add(prop.Name);
            }
            
            // Update local copy
            // This uses the cached property info to avoid expensive reflection
            updated.CopyAllTo(cached, pInfo, null);
        }

        if (!skipEvent)
        {
            // Update
            if (cached is not null)
            {
                eventData.Model = cached;
                // Fire off local event on item
                await cached.InvokeUpdatedEventAsync(eventData);
            }
            // New
            else
            {
                eventData.Model = updated;
                await updated.SyncAsync();
            }
            
            // Fire off global events
            await ModelObserver<TModel>.InvokeAnyUpdated(eventData);

            // printing to console is SLOW, only turn on for debugging reasons
            //Console.WriteLine("Invoked update events for " + updated.Id);
        }
        
        return cached ?? updated;
    }
    
    /// <summary>
    /// Updates an item's properties
    /// </summary>
    public static async Task DeleteItem<TModel, TId>(TModel model) 
        where TModel : ClientModel<TModel, TId>
        where TId : IEquatable<TId>
    {
        var cached = model.TakeAndRemoveFromCache();
        if (cached is null)
        {
            // Invoke static "any" delete
            await model.InvokeDeletedEventAsync();
            await ModelObserver<TModel>.InvokeAnyDeleted(model);
        }
        else
        {
            // Invoke static "any" delete
            await cached.InvokeDeletedEventAsync();
            await ModelObserver<TModel>.InvokeAnyDeleted(cached);
        }
    }
}

public class HashSetPooledObjectPolicy<T> : PooledObjectPolicy<HashSet<T>>
{
    public override HashSet<T> Create() => new HashSet<T>();

    public override bool Return(HashSet<T> obj)
    {
        obj.Clear();
        return true;
    }
}