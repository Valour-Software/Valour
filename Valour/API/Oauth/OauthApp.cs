using Valour.Api.Client;

namespace Valour.Api.Oauth;

public class OauthApp : Valour.Shared.Oauth.OauthApp {
    public static async Task<OauthApp> FindAsync(ulong id) => 
        await ValourClient.GetJsonAsync<OauthApp>($"api/oauth/app/{id}");
}