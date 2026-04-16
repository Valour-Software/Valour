using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

public class UserService : ServiceBase
{
    private readonly LogOptions _logOptions = new(
        "UserService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );
    
    private readonly ValourClient _client;

    public UserService(ValourClient client)
    {
        _client = client;
        SetupLogging(_client.Logger, _logOptions);
    }
    
    public async ValueTask<User> FetchUserAsync(long id, bool skipCache = false)
    {
        if (id == ISharedUser.VictorUserId)
        {
            return User.Victor;
        }
        
        if (!skipCache && _client.Cache.Users.TryGet(id, out var cached))
            return cached;
        
        var response = await _client.PrimaryNode.GetJsonAsync<User>($"{ISharedUser.BaseRoute}/{id}");
        if (!response.Success || response.Data is null)
        {
            LogError($"Failed to fetch user {id}: {response.Message}");

            if (_client.Cache.Users.TryGet(id, out var fallbackCached))
                return fallbackCached;

            return new User(_client)
            {
                Id = id,
                Name = "Unknown User",
                Tag = "0000"
            };
        }

        return response.Data.Sync(_client);
    }

    public async ValueTask<UserProfile> FetchProfileAsync(long userid, bool skipCache)
    {
        if (!skipCache && _client.Cache.UserProfiles.TryGet(userid, out var cached))
            return cached;
        
        var profile = (await _client.PrimaryNode.GetJsonAsync<UserProfile>($"api/userProfiles/{userid}")).Data;

        return profile.Sync(_client);
    }
}
