using Valour.Sdk.Client;
using Valour.Sdk.Services;

namespace Valour.Sdk.ModelLogic;

public static class ModelExtensions
{
    /// <summary>
    /// Syncs all items in the list to the cache and updates
    /// references in the list.
    /// </summary>
    public static List<T> SyncAll<T>(this List<T> list, ValourClient client, ModelInsertFlags flags = ModelInsertFlags.None)
        where T : ClientModel<T>
    {
        if (list is null || list.Count == 0)
            return null;
        
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            list[i] = item.Sync(client, flags);
        }
        
        return list;
    }
    
    /// <summary>
    /// Syncs all items in the array to the cache and updates
    /// references in the array.
    /// </summary>
    public static T[] SyncAll<T>(this T[] array, ValourClient client, ModelInsertFlags flags = ModelInsertFlags.None)
        where T : ClientModel<T>
    {
        if (array is null || array.Length == 0)
            return null;
    
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = array[i].Sync(client, flags);
        }
    
        return array;
    }

    /// <summary>
    /// Syncs all items in the enumerable to the cache and updates
    /// references in the enumerable.
    /// </summary>
    public static IEnumerable<T> SyncAll<T>(
        this IEnumerable<T> source, ValourClient client, ModelInsertFlags flags = ModelInsertFlags.None)
        where T : ClientModel<T>
    {
        if (source is null)
            yield break;

        foreach (var item in source)
        {
            yield return item.Sync(client, flags);
        }
    }
}