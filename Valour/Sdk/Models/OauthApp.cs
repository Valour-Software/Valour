using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class OauthApp : ClientModel<OauthApp, long>, ISharedOauthApp
{
    public override string BaseRoute =>
            $"api/oauthapps";
    
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

    public override OauthApp AddToCache()
    {
        return Client.Cache.OauthApps.Put(Id, this);
    }

    public override OauthApp RemoveFromCache()
    {
        return Client.Cache.OauthApps.TakeAndRemove(Id);
    }
}