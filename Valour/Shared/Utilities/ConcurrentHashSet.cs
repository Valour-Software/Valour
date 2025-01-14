using System.Collections;

namespace Valour.Shared.Utilities;

/// <summary>
/// A high-concurrency HashSet using striped locking.
/// </summary>
public sealed class ConcurrentHashSet<T> : ICollection<T>
{
    private readonly int _stripeCount;
    private readonly object[] _locks;
    private readonly HashSet<T>[] _sets;

    /// <summary>
    /// Initializes a new <see cref="ConcurrentHashSet{T}"/>.
    /// </summary>
    /// <param name="stripeCount">
    /// Number of stripes (locks/shards). Typically, use ~ the number of CPU cores.
    /// Defaults to <see cref="Environment.ProcessorCount"/>.
    /// </param>
    /// <param name="comparer">
    /// Optional equality comparer. If null, <see cref="EqualityComparer{T}.Default"/> is used.
    /// </param>
    public ConcurrentHashSet(int? stripeCount = null, IEqualityComparer<T>? comparer = null)
    {
        if (stripeCount is null or <= 0)
            _stripeCount = Environment.ProcessorCount;
        else
            _stripeCount = stripeCount.Value;

        _locks = new object[_stripeCount];
        _sets = new HashSet<T>[_stripeCount];
        for (int i = 0; i < _stripeCount; i++)
        {
            _locks[i] = new object();
            _sets[i] = new HashSet<T>(comparer);
        }
    }

    /// <summary>
    /// Hash function to map an item to a stripe index.
    /// </summary>
    private int GetStripe(T item)
    {
        // We mask off the sign bit so negative hash codes won't cause negative mods.
        int hash = item?.GetHashCode() ?? 0;
        return (hash & 0x7FFFFFFF) % _stripeCount;
    }

    /// <summary>
    /// Adds an item to the set if not already present.
    /// Returns true if the item was added, false if it was already in the set.
    /// </summary>
    public bool Add(T item)
    {
        int idx = GetStripe(item);
        lock (_locks[idx])
        {
            return _sets[idx].Add(item);
        }
    }

    /// <summary>
    /// Removes an item from the set if it exists.
    /// Returns true if the item was removed, false if it was not found.
    /// </summary>
    public bool Remove(T item)
    {
        int idx = GetStripe(item);
        lock (_locks[idx])
        {
            return _sets[idx].Remove(item);
        }
    }

    /// <summary>
    /// Determines whether the set contains a specific item.
    /// </summary>
    public bool Contains(T item)
    {
        int idx = GetStripe(item);
        lock (_locks[idx])
        {
            return _sets[idx].Contains(item);
        }
    }

    /// <summary>
    /// Clears all items from the set. Thread-safe, but will lock each stripe in turn.
    /// </summary>
    public void Clear()
    {
        // We lock each stripe to clear it.
        for (int i = 0; i < _stripeCount; i++)
        {
            lock (_locks[i])
            {
                _sets[i].Clear();
            }
        }
    }

    /// <summary>
    /// Gets the total count across all stripes.
    /// </summary>
    public int Count
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _stripeCount; i++)
            {
                lock (_locks[i])
                {
                    total += _sets[i].Count;
                }
            }
            return total;
        }
    }

    public bool IsReadOnly => false;

    /// <summary>
    /// Copies all elements to the specified array, beginning at the specified index.
    /// </summary>
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0 || arrayIndex >= array.Length) 
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));

        // We'll collect all items stripe by stripe
        int currentIndex = arrayIndex;
        for (int i = 0; i < _stripeCount; i++)
        {
            lock (_locks[i])
            {
                foreach (var item in _sets[i])
                {
                    if (currentIndex >= array.Length)
                        throw new ArgumentException("The target array is too small.");

                    array[currentIndex++] = item;
                }
            }
        }
    }

    /// <summary>
    /// Adds an item. (ICollection<T> implementation)
    /// </summary>
    void ICollection<T>.Add(T item) => Add(item);

    /// <summary>
    /// Enumerates all items in the set by taking a snapshot of each stripe.
    /// This is safe but does not reflect modifications made during enumeration.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        // For safety, we snapshot each stripe under lock, then yield the combined results.
        List<T> snapshot = new List<T>();
        for (int i = 0; i < _stripeCount; i++)
        {
            lock (_locks[i])
            {
                snapshot.AddRange(_sets[i]);
            }
        }
        return snapshot.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}