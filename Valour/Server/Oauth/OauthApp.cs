using System.ComponentModel.DataAnnotations.Schema;
using Valour.Server.Users;

namespace Valour.Server.Oauth;

public class OauthApp : Shared.Oauth.OauthApp {
    [ForeignKey("Owner_Id")]
    public virtual ServerUser Owner { get; set; }
}