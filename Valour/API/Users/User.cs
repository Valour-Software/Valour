using Valour.Api.Client;
using Valour.Shared;

namespace Valour.Api.Users;

public class User : Shared.Users.User
{
    /// <summary>
    /// Returns the user for the given id
    /// </summary>
    public static async Task<TaskResult<User>> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<User>(id);
            if (cached is not null)
                return new TaskResult<User>(true, "Success: Cached", cached);
        }

        var getResponse = await ValourClient.GetJsonAsync<User>($"api/user/{id}");

        if (getResponse.Success)
            ValourCache.Put(id, getResponse.Data);

        return getResponse;
    }
}

