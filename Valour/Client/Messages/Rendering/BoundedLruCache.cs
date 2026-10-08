namespace Valour.Client.Messages;

/// <summary>
/// Thread-safe cache that evicts the least recently used entry once it holds
/// more than its capacity. Values must be safe to share between callers.
/// </summary>
public sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _lookup;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();
    private readonly object _lock = new();

    public BoundedLruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _lookup = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
    }

    public int Count
    {
        get
        {
            lock (_lock)
                return _lookup.Count;
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (!_lookup.TryGetValue(key, out var node))
            {
                value = default!;
                return false;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_lookup.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _lookup.Remove(key);
            }
            else if (_lookup.Count >= _capacity)
            {
                var oldest = _order.Last!;
                _order.RemoveLast();
                _lookup.Remove(oldest.Value.Key);
            }

            _lookup[key] = _order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
        }
    }
}
