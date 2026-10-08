namespace Valour.Shared.Utilities;

/// <summary>
/// The hybrid event handler allows a given method signature to be called both
/// synchronously and asynchronously. Handlers are stored in copy-on-write arrays,
/// so invoking an event takes no lock and allocates nothing when no asynchronous
/// handlers are registered.
/// </summary>
public class HybridEvent<TEventData> : IDisposable
{
    private static readonly Action<TEventData>[] NoSyncHandlers = [];
    private static readonly Func<TEventData, Task>[] NoAsyncHandlers = [];

    private Action<TEventData>[] _syncHandlers = NoSyncHandlers;
    private Func<TEventData, Task>[] _asyncHandlers = NoAsyncHandlers;

    // Guards writers only. Readers take a snapshot of the array reference.
    private readonly object _writeLock = new();

    public void AddHandler(Action<TEventData> handler)
    {
        lock (_writeLock)
        {
            _syncHandlers = HandlerArrays.Append(_syncHandlers, handler);
        }
    }

    public void AddHandler(Func<TEventData, Task> handler)
    {
        lock (_writeLock)
        {
            _asyncHandlers = HandlerArrays.Append(_asyncHandlers, handler);
        }
    }

    public void RemoveHandler(Action<TEventData> handler)
    {
        lock (_writeLock)
        {
            _syncHandlers = HandlerArrays.Remove(_syncHandlers, handler);
        }
    }

    public void RemoveHandler(Func<TEventData, Task> handler)
    {
        lock (_writeLock)
        {
            _asyncHandlers = HandlerArrays.Remove(_asyncHandlers, handler);
        }
    }

    /// <summary>
    /// Runs synchronous handlers, then starts asynchronous handlers without waiting
    /// for them to finish.
    /// </summary>
    public void Invoke(TEventData data)
    {
        var syncHandlers = Volatile.Read(ref _syncHandlers);

        // One throwing handler must not stop the rest or escape to the caller
        // (on fire-and-forget paths that becomes an unobserved exception, or worse,
        // a fatal one on some hosts).
        for (var i = 0; i < syncHandlers.Length; i++)
        {
            try
            {
                syncHandlers[i].Invoke(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HybridEvent] Sync handler threw: " + ex);
            }
        }

        var asyncHandlers = Volatile.Read(ref _asyncHandlers);
        if (asyncHandlers.Length > 0)
            _ = InvokeAsyncHandlers(asyncHandlers, data);
    }

    private static async Task InvokeAsyncHandlers(Func<TEventData, Task>[] handlers, TEventData data)
    {
        if (handlers.Length == 1)
        {
            try
            {
                var task = handlers[0].Invoke(data);
                if (task is not null)
                    await task;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HybridEvent] Async handler threw: " + ex);
            }

            return;
        }

        var tasks = new List<Task>(handlers.Length);
        for (var i = 0; i < handlers.Length; i++)
        {
            try
            {
                var task = handlers[i].Invoke(data);
                if (task is not null)
                    tasks.Add(task);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HybridEvent] Async handler threw synchronously: " + ex);
            }
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            // Invoke() fire-and-forgets this method, so a faulted handler task
            // would otherwise surface as an unobserved exception.
            Console.WriteLine("[HybridEvent] Async handler threw: " + ex);
        }
    }

    // Enable += and -= operators for adding/removing handlers
    public static HybridEvent<TEventData> operator +(HybridEvent<TEventData> handler, Action<TEventData> action)
    {
        handler ??= new HybridEvent<TEventData>();
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent<TEventData> operator +(HybridEvent<TEventData> handler, Func<TEventData, Task> action)
    {
        handler ??= new HybridEvent<TEventData>();
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent<TEventData> operator -(HybridEvent<TEventData> handler, Action<TEventData> action)
    {
        handler?.RemoveHandler(action);
        return handler;
    }

    public static HybridEvent<TEventData> operator -(HybridEvent<TEventData> handler, Func<TEventData, Task> action)
    {
        handler?.RemoveHandler(action);
        return handler;
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            _syncHandlers = NoSyncHandlers;
            _asyncHandlers = NoAsyncHandlers;
        }
    }
}

/// <summary>
/// The hybrid event handler allows a given method signature to be called both
/// synchronously and asynchronously. Handlers are stored in copy-on-write arrays,
/// so invoking an event takes no lock and allocates nothing when no asynchronous
/// handlers are registered.
/// </summary>
public class HybridEvent : IDisposable
{
    private static readonly Action[] NoSyncHandlers = [];
    private static readonly Func<Task>[] NoAsyncHandlers = [];

    private Action[] _syncHandlers = NoSyncHandlers;
    private Func<Task>[] _asyncHandlers = NoAsyncHandlers;

    // Guards writers only. Readers take a snapshot of the array reference.
    private readonly object _writeLock = new();

    public void AddHandler(Action handler)
    {
        lock (_writeLock)
        {
            _syncHandlers = HandlerArrays.Append(_syncHandlers, handler);
        }
    }

    public void AddHandler(Func<Task> handler)
    {
        lock (_writeLock)
        {
            _asyncHandlers = HandlerArrays.Append(_asyncHandlers, handler);
        }
    }

    public void RemoveHandler(Action handler)
    {
        lock (_writeLock)
        {
            _syncHandlers = HandlerArrays.Remove(_syncHandlers, handler);
        }
    }

    public void RemoveHandler(Func<Task> handler)
    {
        lock (_writeLock)
        {
            _asyncHandlers = HandlerArrays.Remove(_asyncHandlers, handler);
        }
    }

    private void InvokeSyncHandlers()
    {
        var syncHandlers = Volatile.Read(ref _syncHandlers);

        for (var i = 0; i < syncHandlers.Length; i++)
        {
            try
            {
                syncHandlers[i].Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HybridEvent] Sync handler threw: " + ex);
            }
        }
    }

    private static async Task InvokeAsyncHandlers(Func<Task>[] handlers)
    {
        if (handlers.Length == 1)
        {
            try
            {
                var task = handlers[0].Invoke();
                if (task is not null)
                    await task;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HybridEvent] Async handler threw: " + ex);
            }

            return;
        }

        var tasks = new List<Task>(handlers.Length);
        for (var i = 0; i < handlers.Length; i++)
        {
            try
            {
                var task = handlers[i].Invoke();
                if (task is not null)
                    tasks.Add(task);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HybridEvent] Async handler threw synchronously: " + ex);
            }
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[HybridEvent] Async handler threw: " + ex);
        }
    }

    /// <summary>
    /// Schedules synchronous handlers on the thread pool and starts asynchronous
    /// handlers without waiting for them to finish.
    /// </summary>
    public void Invoke()
    {
        if (Volatile.Read(ref _syncHandlers).Length > 0)
            _ = Task.Run(InvokeSyncHandlers);

        var asyncHandlers = Volatile.Read(ref _asyncHandlers);
        if (asyncHandlers.Length > 0)
            _ = InvokeAsyncHandlers(asyncHandlers);
    }

    // Enable += and -= operators for adding/removing handlers
    public static HybridEvent operator +(HybridEvent handler, Action action)
    {
        handler ??= new HybridEvent();
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent operator +(HybridEvent handler, Func<Task> action)
    {
        handler ??= new HybridEvent();
        handler.AddHandler(action);
        return handler;
    }

    public static HybridEvent operator -(HybridEvent handler, Action action)
    {
        handler?.RemoveHandler(action);
        return handler;
    }

    public static HybridEvent operator -(HybridEvent handler, Func<Task> action)
    {
        handler?.RemoveHandler(action);
        return handler;
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            _syncHandlers = NoSyncHandlers;
            _asyncHandlers = NoAsyncHandlers;
        }
    }
}

internal static class HandlerArrays
{
    public static T[] Append<T>(T[] source, T item)
    {
        var result = new T[source.Length + 1];
        Array.Copy(source, result, source.Length);
        result[source.Length] = item;
        return result;
    }

    // Removes the first matching handler, like List<T>.Remove.
    public static T[] Remove<T>(T[] source, T item)
    {
        var index = Array.IndexOf(source, item);
        if (index < 0)
            return source;

        if (source.Length == 1)
            return [];

        var result = new T[source.Length - 1];
        Array.Copy(source, result, index);
        Array.Copy(source, index + 1, result, index, source.Length - index - 1);
        return result;
    }
}
