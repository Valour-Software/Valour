using Valour.Api.Client;

namespace Valour.Api.Items.Users;

public class User : Shared.Items.Users.User<User>
{
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

