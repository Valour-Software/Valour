using Valour.Api.Client;

namespace Valour.Api.Items.Authorization;

public class OauthApp : Valour.Shared.Items.Authorization.OauthApp {
    public static async Task<OauthApp> FindAsync(ulong id) => 
        await ValourClient.GetJsonAsync<OauthApp>($"api/oauth/app/{id}");
}