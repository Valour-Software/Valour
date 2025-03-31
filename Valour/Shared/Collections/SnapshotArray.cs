#nullable enable

namespace Valour.Shared.Collections;

/// <summary>
/// A thread-safe fixed-size array that provides immutable snapshots of its contents.
/// Snapshots are only regenerated when the array is modified.
/// Optimized for scenarios with frequent reads and infrequent writes.
/// </summary>
public class SnapshotArray<T>
{
    private readonly T[] _items;
    private readonly Lock _lock = new();
    private T[] _snapshot;
    private bool _isDirty = true;
    
    /// <summary>
    /// Creates a new SnapshotArray with the specified size.
    /// </summary>
    /// <param name="size">The fixed size of the array.</param>
    public SnapshotArray(int size)
    {
        _items = new T[size];
        _snapshot = new T[size];
    }
    
    /// <summary>
    /// Creates a new SnapshotArray initialized with the provided array.
    /// </summary>
    /// <param name="initialValues">The initial values for the array.</param>
    public SnapshotArray(T[] initialValues)
    {
        _items = new T[initialValues.Length];
        Array.Copy(initialValues, _items, initialValues.Length);
        _snapshot = new T[initialValues.Length];
        _isDirty = true;
    }
    
    /// <summary>
    /// Gets the length of the array.
    /// </summary>
    public int Length => _items.Length;
    
    /// <summary>
    /// Gets an immutable snapshot of the array. The snapshot is only regenerated
    /// when the array has been modified since the last access.
    /// </summary>
    /// <remarks>
    /// This property is thread-safe and can be accessed from multiple threads.
    /// Operations performed on the returned snapshot don't require additional locking.
    /// </remarks>
    public T[] Snapshot
    {
        get
        {
            lock (_lock)
            {
                if (_isDirty)
                {
                    Array.Copy(_items, _snapshot, _items.Length);
                    _isDirty = false;
                }
                return _snapshot;
            }
        }
    }
    
    /// <summary>
    /// Gets an item at the specified index from the snapshot.
    /// </summary>
    /// <param name="index">The index of the item to get.</param>
    /// <returns>The item at the specified index.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown when index is out of range.</exception>
    public T Get(int index)
    {
        if (index < 0 || index >= _items.Length)
            throw new IndexOutOfRangeException($"Index {index} is outside the bounds of the array.");
            
        var currentSnapshot = Snapshot;
        return currentSnapshot[index];
    }
    
    /// <summary>
    /// Safely gets an item at the specified index, returning default if index is out of range.
    /// </summary>
    /// <param name="index">The index of the item to get.</param>
    /// <returns>The item at the specified index, or default if the index is out of range.</returns>
    public T GetSafe(int index)
    {
        if (index < 0 || index >= _items.Length)
            return default!;
            
        var currentSnapshot = Snapshot;
        return currentSnapshot[index];
    }
    
    /// <summary>
    /// Sets an item at the specified index.
    /// </summary>
    /// <param name="index">The index of the item to set.</param>
    /// <param name="value">The value to set.</param>
    /// <exception cref="IndexOutOfRangeException">Thrown when index is out of range.</exception>
    public void Set(int index, T value)
    {
        if (index < 0 || index >= _items.Length)
            throw new IndexOutOfRangeException($"Index {index} is outside the bounds of the array.");
            
        lock (_lock)
        {
            _items[index] = value;
            _isDirty = true;
        }
    }
    
    /// <summary>
    /// Safely sets an item at the specified index, returning false if index is out of range.
    /// </summary>
    /// <param name="index">The index of the item to set.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the item was set, false if the index was out of range.</returns>
    public bool SetSafe(int index, T value)
    {
        if (index < 0 || index >= _items.Length)
            return false;
            
        lock (_lock)
        {
            _items[index] = value;
            _isDirty = true;
            return true;
        }
    }
    
    /// <summary>
    /// Updates multiple items in the array at once under a single lock.
    /// </summary>
    /// <param name="updater">Action that updates the array.</param>
    public void UpdateRange(Action<T[]> updater)
    {
        lock (_lock)
        {
            updater(_items);
            _isDirty = true;
        }
    }
    
    /// <summary>
    /// Clears the array by setting all elements to default(T).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_items, 0, _items.Length);
            _isDirty = true;
        }
    }
    
    /// <summary>
    /// Safe way to read the array without locking on each operation.
    /// </summary>
    /// <param name="action">Action to perform on the snapshot.</param>
    public void ReadSnapshot(Action<T[]> action)
    {
        var currentSnapshot = Snapshot;
        action(currentSnapshot);
    }
    
    /// <summary>
    /// Safe way to read the array and transform it to a result.
    /// </summary>
    /// <typeparam name="TResult">The type of result to return.</typeparam>
    /// <param name="func">Function to transform the snapshot.</param>
    /// <returns>The result of the transformation.</returns>
    public TResult ReadSnapshot<TResult>(Func<T[], TResult> func)
    {
        var currentSnapshot = Snapshot;
        return func(currentSnapshot);
    }
}
