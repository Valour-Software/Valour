using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Valour.Shared.Extensions;
using Valour.Shared.Models;

namespace Valour.Server.Utilities
{
    public class ServerModelCache<TModel, TId> : IEnumerable<TModel>
        where TModel : ServerModel<TId>
        where TId : IEquatable<TId>
    {
        protected readonly List<TModel> List;
        protected Dictionary<TId, TModel> IdMap;
        public IReadOnlyList<TModel> Values;

        protected readonly object _sync = new object();

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return List.Count;
                }
            }
        }
        
        public Dictionary<TId, TModel>.KeyCollection Ids
        {
            get
            {
                lock (_sync)
                {
                    return IdMap.Keys;
                }
            }
        }

        public ServerModelCache(List<TModel> startingList = null)
        {
            // Initialize the structures inside a lock as well
            lock (_sync)
            {
                List = startingList ?? new List<TModel>();
                IdMap = List.ToDictionary(x => x.Id);
                Values = List;
            }
        }

        // Make iterable
        public TModel this[int index]
        {
            get
            {
                lock (_sync)
                {
                    return List[index];
                }
            }
        }

        public IEnumerator<TModel> GetEnumerator()
        {
            lock (_sync)
            {
                // Return a snapshot to avoid enumerating while modified
                return List.ToList().GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public virtual TModel Upsert(TModel model)
        {
            lock (_sync)
            {
                if (IdMap.TryGetValue(model.Id, out var cached))
                {
                    // Copy values and return
                    model.CopyAllTo(cached);
                    return cached; // Now updated
                }
                else
                {
                    // Add and return
                    List.Add(model);
                    IdMap.Add(model.Id, model);
                    return model;
                }
            }
        }

        public virtual void UpsertRange(List<TModel> models)
        {
            lock (_sync)
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
            }
        }

        public virtual void Remove(TModel item)
        {
            lock (_sync)
            {
                if (IdMap.Remove(item.Id))
                {
                    List.Remove(item);
                }
            }
        }
        
        public virtual void Remove(TId id)
        {
            lock (_sync)
            {
                if (IdMap.TryGetValue(id, out var item))
                {
                    IdMap.Remove(id);
                    List.Remove(item);
                }
            }
        }

        public virtual void Set(List<TModel> items)
        {
            lock (_sync)
            {
                // We clear rather than replace the list to ensure that the reference is maintained
                // Because the reference may be used across the application.
                List.Clear();
                IdMap.Clear();

                List.AddRange(items);
                IdMap = List.ToDictionary(x => x.Id);
            }
        }

        public virtual void Clear(bool skipEvent = false)
        {
            lock (_sync)
            {
                List.Clear();
                IdMap.Clear();
            }
        }

        public bool TryGet(TId id, out TModel item)
        {
            lock (_sync)
            {
                return IdMap.TryGetValue(id, out item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TModel item)
        {
            lock (_sync)
            {
                return IdMap.ContainsKey(item.Id); // This is faster than List.Contains
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TId id)
        {
            lock (_sync)
            {
                return IdMap.ContainsKey(id); // This is faster than List.Contains
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Sort(Comparison<TModel> comparison)
        {
            lock (_sync)
            {
                List.Sort(comparison);
            }
        }
    }

    public class SortedServerModelCache<TModel, TId> : ServerModelCache<TModel, TId>
        where TModel : ServerModel<TId>, ISortable
        where TId : IEquatable<TId>
    {
        public override TModel Upsert(TModel model)
        {
            lock (_sync)
            {
                if (IdMap.TryGetValue(model.Id, out var cached))
                {
                    // Check if position changed
                    var oldPosition = cached.GetSortPosition();
                    var newPosition = model.GetSortPosition();

                    if (oldPosition != newPosition)
                    {
                        // Locate old index
                        var cachedIndex = List.BinarySearch(cached, ISortable.Comparer);
                        if (cachedIndex >= 0)
                        {
                            List.RemoveAt(cachedIndex);
                        }

                        // Copy values
                        model.CopyAllTo(cached);

                        // Find new list position
                        var newIndex = List.BinarySearch(cached, ISortable.Comparer);
                        List.Insert(newIndex < 0 ? ~newIndex : newIndex, cached);
                    }
                    else
                    {
                        // Copy values
                        model.CopyAllTo(cached);
                    }

                    return cached; // Now updated
                }
                else
                {
                    // Add and return
                    List.Add(model);
                    IdMap.Add(model.Id, model);

                    // Insert in sorted order, if necessary, or sort after insertion
                    // Since we just added it at the end, we need to put it in the correct place
                    var lastIndex = List.Count - 1;
                    var insertedItem = List[lastIndex];
                    List.RemoveAt(lastIndex);

                    var newIndex = List.BinarySearch(insertedItem, ISortable.Comparer);
                    List.Insert(newIndex < 0 ? ~newIndex : newIndex, insertedItem);

                    return insertedItem;
                }
            }
        }

        public override void Set(List<TModel> items)
        {
            lock (_sync)
            {
                base.Set(items);
                List.Sort(ISortable.Comparer);
            }
        }

        public override void UpsertRange(List<TModel> models)
        {
            lock (_sync)
            {
                // base upsert does not sort
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

                // sort after all models are added rather than after each one
                List.Sort(ISortable.Comparer);
            }
        }
    }
}