using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Api.Client;
using Valour.API.Client;
using Valour.Shared;

namespace Valour.Api.Users;

public class User : Shared.Users.User
{
    /// <summary>
    /// Returns the user for the given id
    /// </summary>
    public static async Task<TaskResult<User>> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh && ValourCache.Users.ContainsKey(id))
            return new TaskResult<User>(true, "Success: Cached", ValourCache.Users[id]);

        var getResponse = await ValourClient.GetJsonAsync<User>($"api/user/{id}");

        if (getResponse.Success)
            ValourCache.Users[id] = getResponse.Data;

        return getResponse;
    }
}

