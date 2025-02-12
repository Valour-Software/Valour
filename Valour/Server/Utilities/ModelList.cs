using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.ObjectPool;
using Valour.Shared.Extensions;
using Valour.Shared.Models;

namespace Valour.Server.Utilities;

public class ModelListSnapshot<TModel, TId>
    where TModel : ServerModel<TId>
    where TId : IEquatable<TId>
{
    public ImmutableList<TModel> List { get; }
    public ImmutableDictionary<TId, TModel> Dictionary { get; }

    public ModelListSnapshot(
        ImmutableList<TModel> list,
        ImmutableDictionary<TId, TModel> dictionary
    )
    {
        List = list;
        Dictionary = dictionary;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TId id)
    {
        return Dictionary.ContainsKey(id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TModel item)
    {
        if (item == null)
            return false; // Or throw ArgumentNullException, depending on desired behavior
        return Dictionary.ContainsKey(item.Id);
    }

    public TModel Get(TId id)
    {
        if (Dictionary.TryGetValue(id, out var model))
        {
            return model;
        }

        return null; // Or throw KeyNotFoundException, depending on desired behavior
    }

    public bool TryGet(TId id, out TModel item)
    {
        return Dictionary.TryGetValue(id, out item);
    }
}

/// <summary>
/// Thread-safe collection for server models that maintains both a list and dictionary
/// for efficient access by index and ID
/// </summary>
public class ServerModelList<TModel, TId> : IEnumerable<TModel>, IDisposable
    where TModel : ServerModel<TId>
    where TId : IEquatable<TId>
{
    // Pools for list and dictionary creation
    private static readonly ObjectPool<List<TModel>> ListPool =
        new DefaultObjectPool<List<TModel>>(new ListPooledObjectPolicy<TModel>());

    private static readonly ObjectPool<Dictionary<TId, TModel>> IdMapPool =
        new DefaultObjectPool<Dictionary<TId, TModel>>(
            new DictionaryPooledObjectPolicy<TId, TModel>()
        );

    protected readonly List<TModel> List;
    protected Dictionary<TId, TModel> IdMap;

    private volatile ModelListSnapshot<TModel, TId> _snapshot;
    protected volatile bool IsDirty;

    protected readonly ReaderWriterLockSlim Lock =
        new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    public int Count
    {
        get
        {
            Lock.EnterReadLock();
            try
            {
                return List.Count;
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of the current list and dictionary state.
    /// This is safe for enumeration and won't block writers.
    /// </summary>
    public ModelListSnapshot<TModel, TId> Snapshot
    {
        get
        {
            if (IsDirty || _snapshot == null)
            {
                Lock.EnterReadLock();
                try
                {
                    if (IsDirty || _snapshot == null)
                    {
                        var listSnapshot = List.ToImmutableList();
                        var dictSnapshot = IdMap.ToImmutableDictionary();
                        _snapshot = new ModelListSnapshot<TModel, TId>(
                            listSnapshot,
                            dictSnapshot
                        );
                        IsDirty = false;
                    }
                }
                finally
                {
                    Lock.ExitReadLock();
                }
            }

            return _snapshot;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TId id)
    {
        Lock.EnterReadLock();
        try
        {
            return IdMap.ContainsKey(id);
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(TModel item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        Lock.EnterReadLock();
        try
        {
            return IdMap.ContainsKey(item.Id);
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

    public IReadOnlyCollection<TId> Ids
    {
        get
        {
            Lock.EnterReadLock();
            try
            {
                return IdMap.Keys.ToImmutableArray();
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }
    }

    public ServerModelList(IEnumerable<TModel> startingItems = null)
    {
        Lock.EnterWriteLock();
        try
        {
            List = startingItems?.ToList() ?? new List<TModel>();
            IdMap = List.ToDictionary(x => x.Id);
            IsDirty = true;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public TModel this[int index]
    {
        get
        {
            Lock.EnterReadLock();
            try
            {
                return List[index];
            }
            finally
            {
                Lock.ExitReadLock();
            }
        }
    }

    public IEnumerator<TModel> GetEnumerator() => Snapshot.List.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public virtual TModel Upsert(TModel model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        Lock.EnterWriteLock();
        try
        {
            if (IdMap.TryGetValue(model.Id, out var cached))
            {
                model.CopyAllTo(cached);
                IsDirty = true;
                return cached;
            }

            List.Add(model);
            IdMap.Add(model.Id, model);
            IsDirty = true;
            return model;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public virtual void UpsertRange(IEnumerable<TModel> models)
    {
        if (models == null)
            throw new ArgumentNullException(nameof(models));

        Lock.EnterWriteLock();
        try
        {
            foreach (var model in models)
            {
                if (IdMap.TryGetValue(model.Id, out var cached))
                {
                    model.CopyAllTo(cached);
                }
                else
                {
                    List.Add(model);
                    IdMap.Add(model.Id, model);
                }
            }

            IsDirty = true;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public virtual bool Remove(TModel item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        Lock.EnterWriteLock();
        try
        {
            if (IdMap.Remove(item.Id))
            {
                List.Remove(item);
                IsDirty = true;
                return true;
            }

            return false;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public virtual bool Remove(TId id)
    {
        Lock.EnterWriteLock();
        try
        {
            if (IdMap.TryGetValue(id, out var item))
            {
                IdMap.Remove(id);
                List.Remove(item);
                IsDirty = true;
                return true;
            }

            return false;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public virtual void Set(IEnumerable<TModel> items)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        Lock.EnterWriteLock();
        try
        {
            List.Clear();
            IdMap.Clear();

            var enumerated = items.ToList();
            List.AddRange(enumerated);

            foreach (var item in enumerated)
            {
                IdMap.Add(item.Id, item);
            }

            IsDirty = true;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public virtual void Clear()
    {
        Lock.EnterWriteLock();
        try
        {
            List.Clear();
            IdMap.Clear();
            IsDirty = true;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public bool TryGet(TId id, out TModel item)
    {
        Lock.EnterReadLock();
        try
        {
            return IdMap.TryGetValue(id, out item);
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

    public TModel Get(TId id)
    {
        Lock.EnterReadLock();
        try
        {
            IdMap.TryGetValue(id, out var model);
            return model;
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Sort(Comparison<TModel> comparison)
    {
        if (comparison == null)
            throw new ArgumentNullException(nameof(comparison));

        Lock.EnterWriteLock();
        try
        {
            List.Sort(comparison);
            IsDirty = true;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        Lock.Dispose();
    }
}

public class SortedServerModelList<TModel, TId> : ServerModelList<TModel, TId>
    where TModel : ServerModel<TId>, ISortable
    where TId : IEquatable<TId>
{
    public override TModel Upsert(TModel model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        Lock.EnterWriteLock();
        try
        {
            if (IdMap.TryGetValue(model.Id, out var cached))
            {
                var oldPosition = cached.GetSortPosition();
                var newPosition = model.GetSortPosition();

                if (oldPosition != newPosition)
                {
                    var cachedIndex = List.BinarySearch(cached, ISortable.Comparer);
                    if (cachedIndex >= 0)
                    {
                        List.RemoveAt(cachedIndex);
                    }

                    model.CopyAllTo(cached);

                    var newIndex = List.BinarySearch(cached, ISortable.Comparer);
                    List.Insert(newIndex < 0 ? ~newIndex : newIndex, cached);
                }
                else
                {
                    model.CopyAllTo(cached);
                }

                IsDirty = true;
                return cached;
            }

            List.Add(model);
            IdMap.Add(model.Id, model);

            var lastIndex = List.Count - 1;
            var insertedItem = List[lastIndex];
            List.RemoveAt(lastIndex);

            var insertIndex = List.BinarySearch(insertedItem, ISortable.Comparer);
            List.Insert(insertIndex < 0 ? ~insertIndex : insertIndex, insertedItem);

            IsDirty = true;
            return insertedItem;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public override void UpsertRange(IEnumerable<TModel> models)
    {
        if (models == null)
            throw new ArgumentNullException(nameof(models));

        Lock.EnterWriteLock();
        try
        {
            foreach (var model in models)
            {
                if (IdMap.TryGetValue(model.Id, out var cached))
                {
                    model.CopyAllTo(cached);
                }
                else
                {
                    List.Add(model);
                    IdMap.Add(model.Id, model);
                }
            }

            List.Sort(ISortable.Comparer);
            IsDirty = true;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public override void Set(IEnumerable<TModel> items)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        Lock.EnterWriteLock();
        try
        {
            List.Clear();
            IdMap.Clear();

            var enumerated = items.ToList();
            List.AddRange(enumerated);

            foreach (var item in enumerated)
            {
                IdMap.Add(item.Id, item);
            }

            List.Sort(ISortable.Comparer);
            IsDirty = true;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }
}