using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Shared.Items;
using Valour.Shared.Items.Users;

namespace Valour.Api.Items.Users;

public class User : UserBase, ISyncedItem<User>
{
    #region Synced Item System

    /// <summary>
    /// Ran when this item is updated
    /// </summary>
    public event Func<Task> OnUpdated;

    /// <summary>
    /// Ran when this item is deleted
    /// </summary>
    public event Func<Task> OnDeleted;

    /// <summary>
    /// Run when any of this item type is updated
    /// </summary>
    public static event Func<User, int, Task> OnAnyUpdated;

    /// <summary>
    /// Run when any of this item type is deleted
    /// </summary>
    public static event Func<User, Task> OnAnyDeleted;

    public async Task InvokeAnyUpdated(User updated, int flags)
    {
        if (OnAnyUpdated != null)
            await OnAnyUpdated?.Invoke(updated, flags);
    }

    public async Task InvokeAnyDeleted(User deleted)
    {
        if (OnAnyDeleted != null)
            await OnAnyDeleted?.Invoke(deleted);
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

    public async Task OnUpdate(int flags)
    {

    }

    #endregion

    public static User Victor = new User()
    {
        Bot = true,
        UserState_Value = 4,
        Pfp_Url = "/media/victor-cyan.png",
        Username = "Victor",
        Valour_Staff = true,
        Id = ulong.MaxValue
    };

    /// <summary>
    /// Returns the user for the given id
    /// </summary>
    public static async Task<User> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<User>(id);
            if (cached is not null)
                return cached;
        }

        var user = await ValourClient.GetJsonAsync<User>($"api/user/{id}");

        if (user is not null)
            await ValourCache.Put(id, user);

        return user;
    }

    public async Task<List<Api.Items.Authorization.OauthApp>> GetOauthAppAsync() =>
        await ValourClient.GetJsonAsync<List<Api.Items.Authorization.OauthApp>>($"api/user/{Id}/apps");
}

