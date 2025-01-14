namespace Valour.Shared.Utilities;

/// <summary>
/// A thread-safe circular buffer (ring buffer) for storing up to a fixed number of items.
/// When at capacity, enqueuing a new item overwrites the oldest item.
/// </summary>
public class ConcurrentCircularBuffer<T>
{
    private readonly T[] _buffer;
    private readonly Lock _syncRoot = new();

    // _head: the next position to write to (enqueue).
    // _tail: the position of the oldest item (dequeue).
    // _count: how many items are currently stored in the buffer.
    private int _head;
    private int _tail;
    private int _count;

    /// <summary>
    /// Creates a concurrent circular buffer with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of items the buffer can hold.</param>
    public ConcurrentCircularBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");

        _buffer = new T[capacity];
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    /// <summary>
    /// The maximum number of items the buffer can hold.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// The current number of items in the buffer.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// Adds an item to the buffer. If the buffer is full, overwrites the oldest item.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Enqueue(T item)
    {
        lock (_syncRoot)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % Capacity;

            if (_count == Capacity)
            {
                // Overwrite the oldest item.
                _tail = (_tail + 1) % Capacity;
            }
            else
            {
                _count++;
            }
        }
    }

    /// <summary>
    /// Tries to remove and return the oldest item from the buffer.
    /// </summary>
    /// <param name="item">The removed item, or default(T) if the buffer is empty.</param>
    /// <returns>True if an item was removed; false if the buffer is empty.</returns>
    public bool TryDequeue(out T item)
    {
        lock (_syncRoot)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            item = _buffer[_tail];
            _tail = (_tail + 1) % Capacity;
            _count--;
            return true;
        }
    }

    /// <summary>
    /// Returns the oldest item without removing it.
    /// </summary>
    /// <param name="item">Oldest item in the buffer.</param>
    /// <returns>True if an item was available; false if the buffer is empty.</returns>
    public bool TryPeek(out T item)
    {
        lock (_syncRoot)
        {
            if (_count == 0)
            {
                item = default!;
                return false;
            }

            item = _buffer[_tail];
            return true;
        }
    }

    /// <summary>
    /// Gets a snapshot of all items in the buffer from oldest to newest.
    /// </summary>
    /// <returns>A list containing all items in oldest-to-newest order.</returns>
    public List<T> ToListAscending()
    {
        lock (_syncRoot)
        {
            var result = new List<T>(_count);
            for (int i = 0; i < _count; i++)
            {
                int index = (_tail + i) % Capacity;
                var item = _buffer[index];
                
                if (item != null)
                {
                    result.Add(item);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Gets a snapshot of all items in the buffer from newest to oldest.
    /// </summary>
    /// <returns>A list containing all items in newest-to-oldest order.</returns>
    public List<T> ToListDescending()
    {
        lock (_syncRoot)
        {
            var result = new List<T>(_count);
            
            // The newest item is at (_head - 1) mod Capacity (if the buffer is not empty).
            // We'll fill the result array from result[0] = newest, to result[_count - 1] = oldest.
            // Start from the index just "behind" _head.
            int newestIndex = (_head - 1 + Capacity) % Capacity;

            for (int i = 0; i < _count; i++)
            {
                int index = (newestIndex - i + Capacity) % Capacity;
                var item = _buffer[index];
                
                if (item != null)
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }
    
    public void RemoveWhere(Predicate<T> match)
    {
        lock (_syncRoot)
        {
            for (int i = 0; i < _count; i++)
            {
                int index = (_tail + i) % Capacity;
                var item = _buffer[index];
                
                if (item != null && match(item))
                {
                    _buffer[index] = default!;
                    _count--;
                }
            }
        }
    }
    
    public void ReplaceWhere(Predicate<T> match, T replacement)
    {
        lock (_syncRoot)
        {
            for (int i = 0; i < _count; i++)
            {
                int index = (_tail + i) % Capacity;
                var item = _buffer[index];
                
                if (item != null && match(item))
                {
                    _buffer[index] = replacement;
                }
            }
        }
    }
    
    public bool Any(Predicate<T> match)
    {
        lock (_syncRoot)
        {
            for (int i = 0; i < _count; i++)
            {
                int index = (_tail + i) % Capacity;
                var item = _buffer[index];
                
                if (item != null && match(item))
                {
                    return true;
                }
            }
            
            return false;
        }
    }

    /// <summary>
    /// Clears the buffer, resetting all pointers.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            // Optionally clear the array to free references for GC:
            // Array.Clear(_buffer, 0, _buffer.Length);

            _head = 0;
            _tail = 0;
            _count = 0;
        }
    }
}