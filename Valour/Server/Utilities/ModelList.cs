using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.ObjectPool;
using Valour.Shared.Extensions;
using Valour.Shared.Models;

namespace Valour.Server.Utilities
{
    public class ServerModelList<TModel, TId> : IEnumerable<TModel>
        where TModel : ServerModel<TId>
        where TId : IEquatable<TId>
    {
        // Pools for list and dictionary creation
        public static readonly ObjectPool<List<TModel>> ListPool = 
            new DefaultObjectPool<List<TModel>>(new ListPooledObjectPolicy<TModel>());
        public static readonly ObjectPool<Dictionary<TId, TModel>> IdMapPool = 
            new DefaultObjectPool<Dictionary<TId, TModel>>(new DictionaryPooledObjectPolicy<TId, TModel>());
        
        protected readonly List<TModel> List;
        protected Dictionary<TId, TModel> IdMap;

        protected readonly object Lock = new object();

        public int Count
        {
            get
            {
                lock (Lock)
                {
                    return List.Count;
                }
            }
        }
        
        /// <summary>
        /// Be very careful using this due to multithreading issues
        /// </summary>
        public IReadOnlyList<TModel> InternalList
        {
            get
            {
                lock (Lock)
                {
                    return List;
                }
            }
        }
        
        public Dictionary<TId, TModel>.KeyCollection Ids
        {
            get
            {
                lock (Lock)
                {
                    return IdMap.Keys;
                }
            }
        }

        public ServerModelList(List<TModel> startingList = null)
        {
            // Initialize the structures inside a lock as well
            lock (Lock)
            {
                List = startingList ?? new List<TModel>();
                IdMap = List.ToDictionary(x => x.Id);
            }
        }

        // Make iterable
        public TModel this[int index]
        {
            get
            {
                lock (Lock)
                {
                    return List[index];
                }
            }
        }

        public IEnumerator<TModel> GetEnumerator()
        {
            lock (Lock)
            {
                // Return a snapshot to avoid enumerating while modified
                return List.ToList().GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        
        /// <summary>
        /// Remember to return the list after use!
        /// </summary>
        public List<TModel> CloneAsList()
        {
            lock (Lock)
            {
                return List.ToList();
            }
        }
        
        /// <summary>
        /// Remember to return the dictionary after use!
        /// </summary>
        public Dictionary<TId, TModel> CloneAsDictionary()
        {
            lock (Lock)
            {
                return new Dictionary<TId, TModel>(IdMap);
            }
        }

        public virtual TModel Upsert(TModel model)
        {
            lock (Lock)
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
        
        public void ReturnList(List<TModel> list)
        {
            ListPool.Return(list);
        }
        
        public void ReturnIdMap(Dictionary<TId, TModel> idMap)
        {
            IdMapPool.Return(idMap);
        }

        public virtual void UpsertRange(List<TModel> models)
        {
            lock (Lock)
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
            lock (Lock)
            {
                if (IdMap.Remove(item.Id))
                {
                    List.Remove(item);
                }
            }
        }
        
        public virtual void Remove(TId id)
        {
            lock (Lock)
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
            lock (Lock)
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
            lock (Lock)
            {
                List.Clear();
                IdMap.Clear();
            }
        }

        public bool TryGet(TId id, out TModel item)
        {
            lock (Lock)
            {
                return IdMap.TryGetValue(id, out item);
            }
        }

        public TModel Get(TId id)
        {
            lock (Lock)
            {
                IdMap.TryGetValue(id, out var model);
                return model;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TModel item)
        {
            lock (Lock)
            {
                return IdMap.ContainsKey(item.Id); // This is faster than List.Contains
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(TId id)
        {
            lock (Lock)
            {
                return IdMap.ContainsKey(id); // This is faster than List.Contains
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Sort(Comparison<TModel> comparison)
        {
            lock (Lock)
            {
                List.Sort(comparison);
            }
        }
    }

    public class SortedServerModelList<TModel, TId> : ServerModelList<TModel, TId>
        where TModel : ServerModel<TId>, ISortable
        where TId : IEquatable<TId>
    {
        public override TModel Upsert(TModel model)
        {
            lock (Lock)
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
            lock (Lock)
            {
                base.Set(items);
                List.Sort(ISortable.Comparer);
            }
        }

        public override void UpsertRange(List<TModel> models)
        {
            lock (Lock)
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