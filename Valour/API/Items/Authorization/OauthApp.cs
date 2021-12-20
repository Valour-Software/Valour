using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;

namespace Valour.Api.Items.Authorization;

public class OauthApp : Item<OauthApp>, ISharedOauthApp {

    /// <summary>
    /// The secret key for the app
    /// </summary>
    [JsonPropertyName("Secret")]
    public string Secret { get; set; }

    /// <summary>
    /// The ID of the user that created this app
    /// </summary>
    [JsonPropertyName("Owner_Id")]
    public ulong Owner_Id { get; set; }

    /// <summary>
    /// The amount of times this app has been used
    /// </summary>
    [JsonPropertyName("Uses")]
    public int Uses { get; set; }

    /// <summary>
    /// The public name for this app
    /// </summary>
    [JsonPropertyName("Name")]
    public string Name { get; set; }

    /// <summary>
    /// The image used to represent the app
    /// </summary>
    [JsonPropertyName("Image_Url")]
    public string Image_Url { get; set; }

    [NotMapped]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.OauthApp;

    public static async Task<OauthApp> FindAsync(ulong id) => 
        await ValourClient.GetJsonAsync<OauthApp>($"api/oauth/app/{id}");
}