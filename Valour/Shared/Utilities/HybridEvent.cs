using Microsoft.Extensions.ObjectPool;

namespace Valour.Shared.Utilities;

/// <summary>
/// The hybrid event handler allows a given method signature to be called both
/// synchronously and asynchronously. Built for efficiency using two separate
/// lists of delegates, with list pooling to minimize allocations.
/// </summary>
public class HybridEvent<TEventData> : IDisposable
{
    // Synchronous and asynchronous handler lists
    private List<Action<TEventData>> _syncHandlers;
    private List<Func<TEventData, Task>> _asyncHandlers;

    // Init is false until the handler lists are initialized
    private bool _init;

    // Lock object for synchronous and asynchronous handler access
    private readonly object _syncLock = new();
    private readonly object _asyncLock = new();

    // Object pool for list reuse
    // This is static because it is shared across all instances of HybridEvent
    private static readonly ObjectPool<List<Action<TEventData>>> SyncListPool = 
        new DefaultObjectPool<List<Action<TEventData>>>(new ListPolicy<Action<TEventData>>());
    private static readonly ObjectPool<List<Func<TEventData, Task>>> AsyncListPool =
        new DefaultObjectPool<List<Func<TEventData, Task>>>(new ListPolicy<Func<TEventData, Task>>());
    
    // Object pool for task list
    private static readonly ObjectPool<List<Task>> TaskListPool =
        new DefaultObjectPool<List<Task>>(new ListPolicy<Task>());

    private void InitIfNeeded()
    {
        if (!_init)
        {
            // set up handler lists
            lock (_syncLock)
            {
                _syncHandlers = SyncListPool.Get();
            }
            
            lock (_asyncLock)
            {
                _asyncHandlers = AsyncListPool.Get();
            }
            
            _init = true;
        }
    }
    
    // Add a synchronous handler
    public void AddHandler(Action<TEventData> handler)
    {
        InitIfNeeded();
        
        lock (_syncLock)
        {
            _syncHandlers.Add(handler);
        }
    }

    // Add an asynchronous handler
    public void AddHandler(Func<TEventData, Task> handler)
    {
        InitIfNeeded();
        
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
        var handlersCopy = SyncListPool.Get();

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
            SyncListPool.Return(handlersCopy);
        }
    }

    // Invoke all asynchronous handlers concurrently with list pooling
    private async Task InvokeAsyncHandlers(TEventData data)
    {
        // Get a pooled list for copying async handlers
        var handlersCopy = AsyncListPool.Get();

        // Copy handlers while locking to prevent concurrent modifications
        lock (_asyncLock)
        {
            handlersCopy.AddRange(_asyncHandlers);  // Copy async handlers into pooled list
        }
        
        var tasks = TaskListPool.Get();

        try
        {
            // Invoke all async handlers in parallel using Task.WhenAll
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
            AsyncListPool.Return(handlersCopy);
            TaskListPool.Return(tasks);
        }
    }

    // Invoke both sync and async handlers
    public void Invoke(TEventData data)
    {
        InvokeSyncHandlers(data);  // Call synchronous handlers first
        _ = InvokeAsyncHandlers(data);  // Then call asynchronous handlers
    }

    // Enable += and -= operators for adding/removing handlers
    public static HybridEvent<TEventData> operator +(HybridEvent<TEventData> handler, Action<TEventData> action)
    {
        if (handler is null)
            handler = new HybridEvent<TEventData>();
        
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent<TEventData> operator +(HybridEvent<TEventData> handler, Func<TEventData, Task> action)
    {
        if (handler is null)
            handler = new HybridEvent<TEventData>();
        
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent<TEventData> operator -(HybridEvent<TEventData> handler, Action<TEventData> action)
    {
        if (handler is null)
            return null;
        
        handler.RemoveHandler(action);
        return handler;
    }

    public static HybridEvent<TEventData> operator -(HybridEvent<TEventData> handler, Func<TEventData, Task> action)
    {
        if (handler is null)
            return null;
        
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

/// <summary>
/// The hybrid event handler allows a given method signature to be called both
/// synchronously and asynchronously. Built for efficiency using two separate
/// lists of delegates, with list pooling to minimize allocations.
/// </summary>
public class HybridEvent : IDisposable
{
    // Synchronous and asynchronous handler lists
    private List<Action> _syncHandlers;
    private List<Func<Task>> _asyncHandlers;

    // Init is false until the handler lists are initialized
    private bool _init;

    // Lock object for synchronous and asynchronous handler access
    private readonly object _syncLock = new();
    private readonly object _asyncLock = new();

    // Object pool for list reuse
    // This is static because it is shared across all instances of HybridEvent
    private static readonly ObjectPool<List<Action>> SyncListPool = 
        new DefaultObjectPool<List<Action>>(new ListPolicy<Action>());
    private static readonly ObjectPool<List<Func<Task>>> AsyncListPool =
        new DefaultObjectPool<List<Func<Task>>>(new ListPolicy<Func<Task>>());
    
    // Object pool for task list
    private static readonly ObjectPool<List<Task>> TaskListPool =
        new DefaultObjectPool<List<Task>>(new ListPolicy<Task>());

    private void InitIfNeeded()
    {
        if (!_init)
        {
            // set up handler lists
            lock (_syncLock)
            {
                _syncHandlers = SyncListPool.Get();
            }
            
            lock (_asyncLock)
            {
                _asyncHandlers = AsyncListPool.Get();
            }
            
            _init = true;
        }
    }
    
    // Add a synchronous handler
    public void AddHandler(Action handler)
    {
        InitIfNeeded();
        
        lock (_syncLock)
        {
            _syncHandlers.Add(handler);
        }
    }

    // Add an asynchronous handler
    public void AddHandler(Func<Task> handler)
    {
        InitIfNeeded();
        
        lock (_asyncLock)
        {
            _asyncHandlers.Add(handler);
        }
    }

    // Remove a synchronous handler
    public void RemoveHandler(Action handler)
    {
        lock (_syncLock)
        {
            _syncHandlers.Remove(handler);
        }
    }

    // Remove an asynchronous handler
    public void RemoveHandler(Func<Task> handler)
    {
        lock (_asyncLock)
        {
            _asyncHandlers.Remove(handler);
        }
    }

    // Invoke all synchronous handlers with list pooling to prevent allocation
    private void InvokeSyncHandlers()
    {
        // Get a pooled list for copying handlers
        var handlersCopy = SyncListPool.Get();

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
                    handlersCopy[i].Invoke(); // No allocations, just iterating over the pooled list
                }
            }
        }
        finally
        {
            // Clear and return the list to the pool
            handlersCopy.Clear();
            SyncListPool.Return(handlersCopy);
        }
    }

    // Invoke all asynchronous handlers concurrently with list pooling
    private async Task InvokeAsyncHandlers()
    {
        // Get a pooled list for copying async handlers
        var handlersCopy = AsyncListPool.Get();

        // Copy handlers while locking to prevent concurrent modifications
        lock (_asyncLock)
        {
            handlersCopy.AddRange(_asyncHandlers);  // Copy async handlers into pooled list
        }
        
        var tasks = TaskListPool.Get();

        try
        {
            // Invoke all async handlers in parallel using Task.WhenAll
            for (int i = 0; i < handlersCopy.Count; i++)
            {
                if (handlersCopy[i] is not null)
                {
                    tasks.Add(handlersCopy[i].Invoke());  // Add tasks to list
                }
            }

            await Task.WhenAll(tasks);  // Wait for all async handlers to complete
        }
        finally
        {
            // Clear and return the list to the pool
            handlersCopy.Clear();
            AsyncListPool.Return(handlersCopy);
            TaskListPool.Return(tasks);
        }
    }

    // Invoke both sync and async handlers
    public void Invoke()
    { 
        _ = Task.Run(InvokeSyncHandlers);  // Call synchronous handlers first
        _ = InvokeAsyncHandlers();  // Then call asynchronous handlers
    }

    // Enable += and -= operators for adding/removing handlers
    public static HybridEvent operator +(HybridEvent handler, Action action)
    {
        if (handler is null)
            handler = new HybridEvent();
        
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent operator +(HybridEvent handler, Func<Task> action)
    {
        if (handler is null)
            handler = new HybridEvent();
        
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent operator -(HybridEvent handler, Action action)
    {
        if (handler is null)
            return null;
        
        handler.RemoveHandler(action);
        return handler;
    }

    public static HybridEvent operator -(HybridEvent handler, Func<Task> action)
    {
        if (handler is null)
            return null;
        
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


