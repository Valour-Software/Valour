using Valour.Sdk.Services;

namespace Valour.Sdk.ModelLogic;

public static class ModelExtensions
{
    /// <summary>
    /// Syncs all items in the list to the cache and updates
    /// references in the list.
    /// </summary>
    public static List<T> SyncAll<T>(this List<T> list, CacheService cache)
        where T : ClientModel<T>
    {
        if (list is null || list.Count == 0)
            return null;
        
        for (int i = 0; i < list.Count; i++)
        {
            list[i] = cache.Sync(list[i]);
        }
        
        return list;
    }
}