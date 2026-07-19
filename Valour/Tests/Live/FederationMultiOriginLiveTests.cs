using Valour.Sdk.Client;
using Valour.Shared.Models;

namespace Valour.Tests.Live;

/// <summary>
/// Drives the real SDK multi-origin path against public hub and community-node
/// deployments. Skipped unless LIVE_FEDERATION=1, so it never runs in CI.
///
/// Env vars: LIVE_FEDERATION=1, LIVE_HUB, LIVE_NODE_DOMAIN, LIVE_EMAIL,
/// LIVE_PASSWORD, LIVE_PLANET (a planet id hosted on the node). Public test
/// runs use HTTPS; LIVE_FEDERATION_INSECURE=1 is only for an isolated local
/// development harness.
/// </summary>
public class FederationMultiOriginLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("LIVE_FEDERATION") == "1";

    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

    private static string RequiredEnv(string key)
    {
        var value = Env(key, "");
        Assert.False(string.IsNullOrWhiteSpace(value), $"Set {key} before running live federation tests.");
        return value;
    }

    [Fact]
    public async Task Client_ConnectsToCommunityNode_AndFetchesPlanetFromIt()
    {
        Assert.SkipUnless(Enabled, "Set LIVE_FEDERATION=1 with public federation test values.");

        var hub = RequiredEnv("LIVE_HUB");
        var nodeDomain = RequiredEnv("LIVE_NODE_DOMAIN");
        var email = RequiredEnv("LIVE_EMAIL");
        var password = RequiredEnv("LIVE_PASSWORD");
        Assert.True(long.TryParse(RequiredEnv("LIVE_PLANET"), out var planetId) && planetId > 0,
            "LIVE_PLANET must be a positive community-hosted planet id.");

        var insecure = string.Equals(Env("LIVE_FEDERATION_INSECURE", ""), "1", StringComparison.Ordinal);
        Assert.True(insecure ||
                    (Uri.TryCreate(hub, UriKind.Absolute, out var hubUri) && hubUri.Scheme == Uri.UriSchemeHttps),
            "LIVE_HUB must use HTTPS unless LIVE_FEDERATION_INSECURE=1 is set for an isolated local harness.");

        var client = new ValourClient(hub);
        client.SetupHttpClient();

        // Log in to the hub and set up the primary (hub) node.
        var login = await client.AuthService.LoginAsync(email, password);
        Assert.True(login.Success, "hub login: " + login.Message);
        var primary = await client.NodeService.SetupPrimaryNodeAsync();
        Assert.True(primary.Success, "primary node: " + primary.Message);

        // The whole point: connect to a DIFFERENT origin (the community node),
        // authenticated by a federation-exchanged token.
        var node = await client.NodeService.ConnectToFederatedNodeAsync(nodeDomain, insecure);
        Assert.NotNull(node);
        Assert.True(node!.IsExternal);
        Assert.Contains(nodeDomain, node.NodeBaseAddress);
        Assert.True(node.IsRealtimeSetup, "external node realtime session did not come up");

        // Fetch a planet that lives ON the node, over the node's own origin.
        var planet = await node.GetJsonAsync<Valour.Sdk.Models.Planet>($"api/planets/{planetId}");
        Assert.True(planet.Success, "fetch planet from node: " + planet.Message);
        Assert.Equal(planetId, planet.Data!.Id);
    }
}
