using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;

namespace Valour.Api.Items;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public abstract class Item<T> : ISharedItem where T : Item<T>
{
    [JsonInclude]
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [NotMapped]
    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public abstract ItemType ItemType { get; }

    /// <summary>
    /// Run when any of this item type is updated
    /// </summary>
    public static event Func<T, int, Task> OnAnyUpdated;

    /// <summary>
    /// Run when any of this item type is deleted
    /// </summary>
    public static event Func<T, Task> OnAnyDeleted;

    /// <summary>
    /// Ran when this item is updated
    /// </summary>
    public event Func<Task> OnUpdated;

    /// <summary>
    /// Ran when this item is deleted
    /// </summary>
    public event Func<Task> OnDeleted;

    public async virtual Task OnUpdate(int flags)
    {

    }

    public async Task InvokeUpdated(int flags)
    {
        await OnUpdate(flags);

        if (OnUpdated != null)
            await OnUpdated?.Invoke();
    }

    public async Task InvokeDeleted()
    {
        if (OnDeleted != null)
            await OnDeleted?.Invoke();
    }

    public async Task InvokeAnyUpdated(T updated, int flags)
    {
        if (OnAnyUpdated != null)
            await OnAnyUpdated?.Invoke(updated, flags);
    }

    public async Task InvokeAnyDeleted(T deleted)
    {
        if (OnAnyDeleted != null)
            await OnAnyDeleted?.Invoke(deleted);
    }


    // Override equality so that equal ID is seen as the same object.

    public override bool Equals(object obj)
    {
        T con = obj as T;

        if (con != null)
            return con.Id == this.Id;

        return false;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}

