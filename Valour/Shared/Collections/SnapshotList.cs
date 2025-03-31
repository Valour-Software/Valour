#nullable enable

using System.Collections;
using System.Collections.Immutable;

namespace Valour.Shared.Collections;

/// <summary>
/// A thread-safe collection that provides immutable snapshots of its contents.
/// Snapshots are only regenerated when the collection is modified.
/// Optimized for scenarios with frequent reads and infrequent writes.
/// </summary>
public class SnapshotList<T> : IReadOnlyCollection<T>
{
    private readonly List<T> _items = new();
    private readonly Lock _lock = new();
    private ImmutableList<T> _snapshot = ImmutableList<T>.Empty;
    private bool _isDirty = true;

    /// <summary>
    /// Gets an immutable snapshot of the collection. The snapshot is only regenerated
    /// when the collection has been modified since the last access.
    /// </summary>
    /// <remarks>
    /// This property is thread-safe and can be accessed from multiple threads.
    /// Operations performed on the returned snapshot don't require additional locking.
    /// </remarks>
    public ImmutableList<T> Snapshot
    {
        get
        {
            lock (_lock)
            {
                if (_isDirty)
                {
                    _snapshot = _items.ToImmutableList();
                    _isDirty = false;
                }
                return _snapshot;
            }
        }
    }

    /// <summary>
    /// Forces creation of a new snapshot and returns it.
    /// </summary>
    /// <returns>A fresh immutable snapshot of the collection's current state.</returns>
    public ImmutableList<T> CreateFreshSnapshot()
    {
        lock (_lock)
        {
            _snapshot = _items.ToImmutableList();
            _isDirty = false;
            return _snapshot;
        }
    }

    /// <summary>
    /// Gets the number of items in the collection.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// Adds an item to the collection if it doesn't already exist.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <returns>True if the item was added, false if it already existed.</returns>
    public bool Add(T item)
    {
        lock (_lock)
        {
            if (!_items.Contains(item))
            {
                _items.Add(item);
                _isDirty = true;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Adds multiple items to the collection, skipping any that already exist.
    /// </summary>
    /// <param name="items">The items to add.</param>
    /// <returns>The number of items that were actually added.</returns>
    public int AddRange(IEnumerable<T> items)
    {
        lock (_lock)
        {
            int added = 0;
            foreach (var item in items)
            {
                if (!_items.Contains(item))
                {
                    _items.Add(item);
                    added++;
                }
            }
            
            if (added > 0)
            {
                _isDirty = true;
            }
            
            return added;
        }
    }

    /// <summary>
    /// Removes an item from the collection.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    /// <returns>True if the item was removed, false if it wasn't found.</returns>
    public bool Remove(T item)
    {
        lock (_lock)
        {
            bool result = _items.Remove(item);
            if (result)
            {
                _isDirty = true;
            }
            return result;
        }
    }

    /// <summary>
    /// Removes all items that match the specified predicate.
    /// </summary>
    /// <param name="match">The predicate to match items against.</param>
    /// <returns>The number of items removed.</returns>
    public int RemoveAll(Predicate<T> match)
    {
        lock (_lock)
        {
            int count = _items.RemoveAll(match);
            if (count > 0)
            {
                _isDirty = true;
            }
            return count;
        }
    }

    /// <summary>
    /// Clears all items from the collection.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (_items.Count > 0)
            {
                _items.Clear();
                
                // Set snapshot to empty
                // There is no need to set dirty since we know the end result
                _snapshot = ImmutableList<T>.Empty;
            }
        }
    }

    /// <summary>
    /// Checks if the collection contains a specific item.
    /// </summary>
    /// <param name="item">The item to check for.</param>
    /// <returns>True if the item exists in the collection, otherwise false.</returns>
    public bool Contains(T item)
    {
        lock (_lock)
        {
            return _items.Contains(item);
        }
    }

    /// <summary>
    /// Performs an action on each item in the collection under a lock.
    /// </summary>
    /// <param name="action">The action to perform on each item.</param>
    public void ForEach(Action<T> action)
    {
        lock (_lock)
        {
            foreach (var item in _items)
            {
                action(item);
            }
        }
    }

    /// <summary>
    /// Safe way to read the collection without locking on each operation.
    /// </summary>
    /// <param name="action">Action to perform on the snapshot.</param>
    public void ReadSnapshot(Action<ImmutableList<T>> action)
    {
        // Capture the snapshot once to prevent multiple locks
        var currentSnapshot = Snapshot;
        action(currentSnapshot);
    }

    /// <summary>
    /// Safe way to read the collection and transform it to a result.
    /// </summary>
    /// <typeparam name="TResult">The type of result to return.</typeparam>
    /// <param name="func">Function to transform the snapshot.</param>
    /// <returns>The result of the transformation.</returns>
    public TResult ReadSnapshot<TResult>(Func<ImmutableList<T>, TResult> func)
    {
        var currentSnapshot = Snapshot;
        return func(currentSnapshot);
    }

    /// <summary>
    /// Updates the collection using a lock for thread safety.
    /// </summary>
    /// <param name="updateAction">Action that updates the collection.</param>
    public void Update(Action<List<T>> updateAction)
    {
        lock (_lock)
        {
            updateAction(_items);
            _isDirty = true;
        }
    }

    /// <summary>
    /// Implements GetEnumerator to support foreach loops.
    /// Returns an enumerator for the current snapshot.
    /// </summary>
    public IEnumerator<T> GetEnumerator() => Snapshot.GetEnumerator();

    /// <summary>
    /// Implements IEnumerable.GetEnumerator
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
