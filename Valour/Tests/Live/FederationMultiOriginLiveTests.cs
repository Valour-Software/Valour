using Valour.Sdk.Client;
using Valour.Shared.Models;

namespace Valour.Tests.Live;

/// <summary>
/// Drives the real SDK multi-origin path against two LIVE instances (a hub on
/// :5100 and a community node on :5200). Skipped unless LIVE_FEDERATION=1, so
/// it never runs in CI — it needs the two-instance Docker harness up.
///
/// Env vars: LIVE_FEDERATION=1, LIVE_HUB (default http://localhost:5100/),
/// LIVE_NODE_DOMAIN (default localhost:5200), LIVE_EMAIL, LIVE_PASSWORD,
/// LIVE_PLANET (a planet id hosted on the node).
/// </summary>
public class FederationMultiOriginLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("LIVE_FEDERATION") == "1";

    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

    [Fact]
    public async Task Client_ConnectsToCommunityNode_AndFetchesPlanetFromIt()
    {
        Assert.SkipUnless(Enabled, "Set LIVE_FEDERATION=1 with the two-instance harness up.");

        var hub = Env("LIVE_HUB", "http://localhost:5100/");
        var nodeDomain = Env("LIVE_NODE_DOMAIN", "localhost:5200");
        var email = Env("LIVE_EMAIL", "");
        var password = Env("LIVE_PASSWORD", "");
        var planetId = long.Parse(Env("LIVE_PLANET", "0"));

        var client = new ValourClient(hub);
        client.SetupHttpClient();

        // Log in to the hub and set up the primary (hub) node.
        var login = await client.AuthService.LoginAsync(email, password);
        Assert.True(login.Success, "hub login: " + login.Message);
        var primary = await client.NodeService.SetupPrimaryNodeAsync();
        Assert.True(primary.Success, "primary node: " + primary.Message);

        // The whole point: connect to a DIFFERENT origin (the community node),
        // authenticated by a federation-exchanged token.
        var node = await client.NodeService.ConnectToFederatedNodeAsync(nodeDomain, insecure: true);
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
