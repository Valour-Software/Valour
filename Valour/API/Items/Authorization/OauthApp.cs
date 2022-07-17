using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;

namespace Valour.Api.Items.Authorization;

public class OauthApp : ISharedOauthApp {

    public long Id {get; set; }

    /// <summary>
    /// The secret key for the app
    /// </summary>
    public string Secret { get; set; }

    /// <summary>
    /// The ID of the user that created this app
    /// </summary>
    public long OwnerId { get; set; }

    /// <summary>
    /// The amount of times this app has been used
    /// </summary>
    public int Uses { get; set; }

    /// <summary>
    /// The image used to represent the app
    /// </summary>
    public string ImageUrl { get; set; }

    /// <summary>
    /// The name of the app
    /// </summary>
    public string Name { get; set; }

    public static async Task<OauthApp> FindAsync(ulong id) => 
        (await ValourClient.GetJsonAsync<OauthApp>($"api/oauth/app/{id}")).Data;
}