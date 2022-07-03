using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Authorization;
using Valour.Shared.Items.Users;

namespace Valour.Api.Items.Users;

public class User : Item, ISharedUser
{
    [JsonIgnore]
    public static User Victor = new User()
    {
        Bot = true,
        UserStateCode = 4,
        PfpUrl = "/media/victor-cyan.png",
        Name = "Victor",
        ValourStaff = true,
        Id = ulong.MaxValue
    };

    /// <summary>
    /// The url for the user's profile picture
    /// </summary>
    public string PfpUrl { get; set; }

    /// <summary>
    /// The Date and Time that the user joined Valour
    /// </summary>
    public DateTime Joined { get; set; }

    /// <summary>
    /// The name of this user
    /// </summary>
    public string Name { get; set; }

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
    public DateTime LastActive { get; set; }

    public TimeSpan LastActiveSpan =>
        ISharedUser.GetLastActiveSpan(this);

    public UserState UserState
    {
        get => ISharedUser.GetUserState(this);
        set => ISharedUser.SetUserState(this, value);
    }

    public async Task<List<OauthApp>> GetOauthAppAsync() =>
        await ValourClient.GetJsonAsync<List<OauthApp>>($"api/user/{Id}/apps");
}

