using Microsoft.Extensions.ObjectPool;

namespace Valour.Client.Utility;

/// <summary>
/// The hybrid event handler allows a given method signature to be called both
/// synchronously and asynchronously. Built for efficiency using two separate
/// lists of delegates, with list pooling to minimize allocations.
/// </summary>
public class HybridEvent<TEventData> : IDisposable
{
    // Synchronous and asynchronous handler lists
    private readonly List<Action<TEventData>> _syncHandlers = new();
    private readonly List<Func<TEventData, Task>> _asyncHandlers = new();

    // Lock object for synchronous and asynchronous handler access
    private readonly object _syncLock = new();
    private readonly object _asyncLock = new();

    // Object pool for list reuse
    private readonly ObjectPool<List<Action<TEventData>>> _syncListPool;
    private readonly ObjectPool<List<Func<TEventData, Task>>> _asyncListPool;

    public HybridEvent()
    {
        // Initialize object pools for reusing lists
        _syncListPool = new DefaultObjectPool<List<Action<TEventData>>>(new ListPolicy<Action<TEventData>>());
        _asyncListPool = new DefaultObjectPool<List<Func<TEventData, Task>>>(new ListPolicy<Func<TEventData, Task>>());
    }

    // Add a synchronous handler
    public void AddHandler(Action<TEventData> handler)
    {
        lock (_syncLock)
        {
            _syncHandlers.Add(handler);
        }
    }

    // Add an asynchronous handler
    public void AddHandler(Func<TEventData, Task> handler)
    {
        lock (_asyncLock)
        {
            _asyncHandlers.Add(handler);
        }
    }

    // Remove a synchronous handler
    public void RemoveHandler(Action<TEventData> handler)
    {
        lock (_syncLock)
        {
            _syncHandlers.Remove(handler);
        }
    }

    // Remove an asynchronous handler
    public void RemoveHandler(Func<TEventData, Task> handler)
    {
        lock (_asyncLock)
        {
            _asyncHandlers.Remove(handler);
        }
    }

    // Invoke all synchronous handlers with list pooling to prevent allocation
    private void InvokeSyncHandlers(TEventData data)
    {
        // Get a pooled list for copying handlers
        var handlersCopy = _syncListPool.Get();

        // Copy handlers while locking to prevent concurrent modifications
        lock (_syncLock)
        {
            handlersCopy.AddRange(_syncHandlers);  // Copy handlers into pooled list
        }

        try
        {
            // Invoke all handlers
            for (int i = 0; i < handlersCopy.Count; i++)
            {
                if (handlersCopy[i] is not null)
                {
                    handlersCopy[i].Invoke(data); // No allocations, just iterating over the pooled list
                }
            }
        }
        finally
        {
            // Clear and return the list to the pool
            handlersCopy.Clear();
            _syncListPool.Return(handlersCopy);
        }
    }

    // Invoke all asynchronous handlers concurrently with list pooling
    private async Task InvokeAsyncHandlers(TEventData data)
    {
        // Get a pooled list for copying async handlers
        var handlersCopy = _asyncListPool.Get();

        // Copy handlers while locking to prevent concurrent modifications
        lock (_asyncLock)
        {
            handlersCopy.AddRange(_asyncHandlers);  // Copy async handlers into pooled list
        }

        try
        {
            // Invoke all async handlers in parallel using Task.WhenAll
            var tasks = new List<Task>(handlersCopy.Count);
            for (int i = 0; i < handlersCopy.Count; i++)
            {
                if (handlersCopy[i] is not null)
                {
                    tasks.Add(handlersCopy[i].Invoke(data));  // Add tasks to list
                }
            }

            await Task.WhenAll(tasks);  // Wait for all async handlers to complete
        }
        finally
        {
            // Clear and return the list to the pool
            handlersCopy.Clear();
            _asyncListPool.Return(handlersCopy);
        }
    }

    // Invoke both sync and async handlers
    public async Task Invoke(TEventData data)
    {
        InvokeSyncHandlers(data);  // Call synchronous handlers first
        await InvokeAsyncHandlers(data);  // Then call asynchronous handlers
    }

    // Enable += and -= operators for adding/removing handlers
    public static HybridEvent<TEventData> operator +(HybridEvent<TEventData> handler, Action<TEventData> action)
    {
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent<TEventData> operator +(HybridEvent<TEventData> handler, Func<TEventData, Task> action)
    {
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent<TEventData> operator -(HybridEvent<TEventData> handler, Action<TEventData> action)
    {
        handler.RemoveHandler(action);
        return handler;
    }

    public static HybridEvent<TEventData> operator -(HybridEvent<TEventData> handler, Func<TEventData, Task> action)
    {
        handler.RemoveHandler(action);
        return handler;
    }
    
    // Cleanup everything
    public void Dispose()
    {
        _syncHandlers.Clear();
        _asyncHandlers.Clear();
    }

    // Custom object pooling policy for List<T>
    private class ListPolicy<T> : PooledObjectPolicy<List<T>>
    {
        public override List<T> Create() => new List<T>();
        public override bool Return(List<T> obj)
        {
            obj.Clear();  // Clear the list before returning to pool
            return true;
        }
    }
}

