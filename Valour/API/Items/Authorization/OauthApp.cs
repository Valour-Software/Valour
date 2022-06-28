using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;

namespace Valour.Api.Items.Authorization;

public class OauthApp : Shared.Items.Authorization.OauthApp {
    public static async Task<OauthApp> FindAsync(ulong id) => 
        await ValourClient.GetJsonAsync<OauthApp>($"api/oauth/app/{id}");
}