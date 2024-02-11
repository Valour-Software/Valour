using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class User : LiveModel, ISharedUser
{
    #region IPlanetModel implementation

    public override string BaseRoute =>
            $"api/users";

    #endregion

    [JsonIgnore]
    public static User Victor = new User()
    {
        Bot = true,
        UserStateCode = 4,
        PfpUrl = "/media/victor-cyan.png",
        Name = "Victor",
        ValourStaff = true,
        Id = long.MaxValue
    };

    /// <summary>
    /// The url for the user's profile picture
    /// </summary>
    public string PfpUrl { get; set; }

    /// <summary>
    /// The Date and Time that the user joined Valour
    /// </summary>
    public DateTime TimeJoined { get; set; }

    /// <summary>
    /// The name of this user
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The tag (discriminator) of this user
    /// </summary>
    public string Tag { get; set; }

    /// <summary>
    /// True if the user is a bot
    /// </summary>
    public bool Bot { get; set; }

    /// <summary>
    /// True if the account has been disabled
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// True if this user is a member of the Valour official staff team. Falsely modifying this 
    /// through a client modification to present non-official staff as staff is a breach of our
    /// license. Don't do that.
    /// </summary>
    public bool ValourStaff { get; set; }

    /// <summary>
    /// The user's currently set status - this could represent how they feel, their disdain for the political climate
    /// of the modern world, their love for their mother's cooking, or their hate for lazy programmers.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// The integer representation of the current user state
    /// </summary>
    public int UserStateCode { get; set; }

    /// <summary>
    /// The last time this user was flagged as active (successful auth)
    /// </summary>
    public DateTime TimeLastActive { get; set; }

    /// <summary>
    /// True if the user has been recently on a mobile device
    /// </summary>
    public bool IsMobile { get; set; }
    
    /// <summary>
    /// If the user has completed the compliance step for regulatory purposes.
    /// This should only ever be false on legacy or testing accounts.
    /// </summary>
    public bool Compliance { get; set; }
    
    /// <summary>
    /// If not null, the type of UserSubscription the user currently
    /// is subscribed to
    /// </summary>
    public string SubscriptionType { get; set; }
    
    /// <summary>
    /// The subscription the user currently has
    /// </summary>
    [JsonIgnore]
    public UserSubscriptionType Subscription =>
        ISharedUser.GetSubscription(this);
    
    [JsonIgnore]
    public string NameAndTag => $"{Name}#{Tag}";

    public TimeSpan LastActiveSpan =>
        ISharedUser.GetLastActiveSpan(this);

    public UserState UserState
    {
        get => ISharedUser.GetUserState(this);
        set => ISharedUser.SetUserState(this, value);
    }

    public async Task<List<OauthApp>> GetOauthAppAsync() =>
        (await ValourClient.PrimaryNode.GetJsonAsync<List<OauthApp>>($"api/users/{Id}/apps")).Data;

    public static async ValueTask<User> FindAsync(long id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<User>(id);
            if (cached is not null)
                return cached;
        }

        var item = (await ValourClient.PrimaryNode.GetJsonAsync<User>($"api/users/{id}")).Data;

        if (item is not null)
        {
            await ValourCache.Put(id, item);
        }

        return item;
    }
    
    public async Task<TaskResult<UserProfile>> GetProfileAsync() =>
        await ValourClient.PrimaryNode.GetJsonAsync<UserProfile>($"api/userProfiles/{Id}");

    public async Task<TaskResult<List<User>>> GetFriendsAsync()
        => await ValourClient.PrimaryNode.GetJsonAsync<List<User>>($"api/users/{Id}/friends");

    public async Task<TaskResult<UserFriendData>> GetFriendDataAsync()
        => await ValourClient.PrimaryNode.GetJsonAsync<UserFriendData>($"api/users/{Id}/frienddata");

    public string GetFailedPfpUrl()
    {
        int var = (int)(Id % 5);
        return "_content/Valour.Client/media/user-icons/icon-" + var + ".png";
    }
}

