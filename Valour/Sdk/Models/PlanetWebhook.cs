using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// An incoming webhook bound to a planet channel. Managing webhooks requires
/// the ManageWebhooks planet permission; executing one only requires the
/// token URL (see <see cref="Client.WebhookClient"/>).
/// </summary>
public class PlanetWebhook : ClientPlanetModel<PlanetWebhook, long>, ISharedPlanetWebhook
{
    public override string BaseRoute => ISharedPlanetWebhook.BaseRoute;

    public long PlanetId { get; set; }

    /// <summary>
    /// The channel this webhook posts to
    /// </summary>
    public long ChannelId { get; set; }

    /// <summary>
    /// The default display name for messages sent by this webhook
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The default avatar for messages sent by this webhook
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// The secret execute token. Only present in create, rotate, and
    /// get-by-id responses for members with ManageWebhooks; null in
    /// live-sync broadcasts.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// The user who created the webhook
    /// </summary>
    public long CreatorUserId { get; set; }

    public DateTime TimeCreated { get; set; }

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private PlanetWebhook() : base() { }
    public PlanetWebhook(ValourClient client) : base(client) { }

    /// <summary>
    /// The execute URL for this webhook, or null when the token is unknown.
    /// </summary>
    public string? GetExecuteUrl() =>
        Token is null ? null : $"{Client.BaseAddress}{ISharedPlanetWebhook.GetExecuteRoute(Id, Token)}";

    /// <summary>
    /// Replaces this webhook's token, invalidating the old one.
    /// </summary>
    public async Task<TaskResult<PlanetWebhook>> RotateTokenAsync()
    {
        var result = await Node.PostAsyncWithResponse<PlanetWebhook>($"{IdRoute}/rotate", null);
        if (result.Success && result.Data is not null)
            Token = result.Data.Token;

        return result;
    }

    public override PlanetWebhook AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        var planet = GetPlanet(false);
        if (planet is null)
            return this;

        return Planet.Webhooks.Put(this, flags);
    }

    public override PlanetWebhook RemoveFromCache(bool skipEvents = false)
    {
        return Planet.Webhooks.Remove(this, skipEvents);
    }
}
