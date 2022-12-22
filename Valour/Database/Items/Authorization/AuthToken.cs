using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Server.Database.Items.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

namespace Valour.Database.Items.Authorization;

[Table("auth_tokens")]
public class AuthToken : ISharedAuthToken
{
    [Key]
    [Column("id")]
    public string Id { get; set; }

    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }

    /// <summary>
    /// The ID of the app that has been issued this token
    /// </summary>
    [Column("app_id")]
    public string AppId { get; set; }

    /// <summary>
    /// The user that this token is valid for
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// The scope of the permissions this token is valid for
    /// </summary>
    [Column("scope")]
    public long Scope { get; set; }

    /// <summary>
    /// The time that this token was issued
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time that this token will expire
    /// </summary>
    [Column("time_expires")]
    public DateTime TimeExpires { get; set; }

    /// <summary>
    /// The IP address this token was issued to originally
    /// </summary>
    [Column("issued_address")]
    public string IssuedAddress { get; set; }

    /// <summary>
    /// Returns whether the auth token has the given scope
    /// </summary>
    public bool HasScope(Permission permission) =>
        ISharedAuthToken.HasScope(permission, this);
    
}

