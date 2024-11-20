using Valour.Shared.Models;

namespace Valour.Server.Models;

public class OauthApp : ServerModel<long>, ISharedOauthApp
{
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
    /// The redirect url for authorization
    /// </summary>
    public string RedirectUrl { get; set; }
}