using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Shared.Items;
using Valour.Shared.Items.Users;

namespace Valour.Api.Items.Users;

public class User : NamedItem<User>, ISharedUser
{
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
    /// The main display name for the user
    /// </summary>
    [JsonPropertyName("Username")]
    public string Username { get; set; }

    /// <summary>
    /// The url for the user's profile picture
    /// </summary>
    [JsonPropertyName("Pfp_Url")]
    public string Pfp_Url { get; set; }

    /// <summary>
    /// The Date and Time that the user joined Valour
    /// </summary>
    [JsonPropertyName("Join_DateTime")]
    public DateTime Join_DateTime { get; set; }

    /// <summary>
    /// True if the user is a bot
    /// </summary>
    [JsonPropertyName("Bot")]
    public bool Bot { get; set; }

    /// <summary>
    /// True if the account has been disabled
    /// </summary>
    [JsonPropertyName("Disabled")]
    public bool Disabled { get; set; }

    /// <summary>
    /// True if this user is a member of the Valour official staff team. Falsely modifying this 
    /// through a client modification to present non-official staff as staff is a breach of our
    /// license. Don't do that.
    /// </summary>
    [JsonPropertyName("Valour_Staff")]
    public bool Valour_Staff { get; set; }

    /// <summary>
    /// The user's currently set status - this could represent how they feel, their disdain for the political climate
    /// of the modern world, their love for their mother's cooking, or their hate for lazy programmers.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// The integer representation of the current user state
    /// </summary>
    [JsonPropertyName("UserState_Value")]
    public int UserState_Value { get; set; }

    /// <summary>
    /// The last time this user was flagged as active (successful auth)
    /// </summary>
    [JsonPropertyName("Last_Active")]
    public DateTime Last_Active { get; set; }

    /// <summary>
    /// The span of time from which the user was last active
    /// </summary>
    [NotMapped]
    [JsonPropertyName("Last_Active_Span")]
    public TimeSpan Last_Active_Span => ((ISharedUser)this).Last_Active_Span;
    

    [NotMapped]
    public UserState UserState => ((ISharedUser)this).UserState;
    

    [NotMapped]
    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.User;

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

