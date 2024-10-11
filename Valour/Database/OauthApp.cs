using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("oauth_apps")]
public class OauthApp : Model, ISharedOauthApp
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("OwnerId")]
    public virtual User Owner { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The secret key for the app
    /// </summary>
    [Column("secret")]
    public string Secret { get; set; }

    /// <summary>
    /// The ID of the user that created this app
    /// </summary>
    [Column("owner_id")]
    public long OwnerId { get; set; }

    /// <summary>
    /// The amount of times this app has been used
    /// </summary>
    [Column("uses")]
    public int Uses { get; set; }

    /// <summary>
    /// The image used to represent the app
    /// </summary>
    [Column("image_url")]
    public string ImageUrl { get; set; }

    /// <summary>
    /// The name of the app
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// The redirect url for authorization
    /// </summary>
    [Column("redirect_url")]
    public string RedirectUrl { get; set; }
}