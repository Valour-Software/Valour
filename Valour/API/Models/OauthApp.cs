using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Api.Nodes;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Api.Models;

public class OauthApp : Item, ISharedOauthApp
{
    #region IPlanetModel implementation

    public override string BaseRoute =>
            $"api/oauthapps";

    #endregion

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

    /// <summary>
    /// The redirect for this app's authorization
    /// </summary>
    public string RedirectUrl { get; set; }

    public static async Task<OauthApp> FindAsync(long id) =>
        (await NodeManager.Nodes.First().GetJsonAsync<OauthApp>($"api/oauth/app/{id}")).Data;

    public static async Task<PublicOauthAppData> FindPublicDataAsync(long id) =>
        (await NodeManager.Nodes.First().GetJsonAsync<PublicOauthAppData>($"api/oauth/app/public/{id}")).Data;
}