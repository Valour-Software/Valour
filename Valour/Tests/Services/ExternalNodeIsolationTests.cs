using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Sdk.Nodes;
using Valour.Sdk.Services;
using Valour.Sdk.Utility;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

public class ExternalNodeIsolationTests
{
    [Fact]
    public void ExternalUnauthorizedResponse_ExpiresOnlyTheFederatedSession()
    {
        var client = new ValourClient("https://hub.example/");
        client.AuthService.SetToken("hub-session");
        var node = CreateExternalNode(client);

        InvokeUnauthorizedResponse(node);

        Assert.Equal("hub-session", client.AuthService.Token);
        Assert.True(node.NeedsFederationSessionRefresh);
    }

    [Fact]
    public void ExternalRealtimeEvents_AreLimitedToExplicitlyJoinedPlanets()
    {
        var node = CreateExternalNode(new ValourClient("https://hub.example/"));

        Assert.False(AcceptsPlanetEvent(node, 42));

        var subscriptions = (ConcurrentDictionary<long, byte>)typeof(Node)
            .GetField("_realtimePlanets", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(node)!;
        subscriptions.TryAdd(42, 1);

        Assert.True(AcceptsPlanetEvent(node, 42));
        Assert.False(AcceptsPlanetEvent(node, 43));
        Assert.False(AcceptsPlanetEvent(node, null));
    }

    [Fact]
    public void ExternalMemberUserSnapshot_DoesNotOverwriteHubUserCache()
    {
        var client = new ValourClient("https://hub.example/");
        var node = CreateExternalNode(client);
        SetNodeName(node, "community.example");
        client.NodeService.RegisterNode(node);
        client.NodeService.SetKnownByPlanet(42, node.Name);

        new Planet(client) { Id = 42, NodeName = node.Name }.Sync(client);
        new User(client) { Id = 9, Name = "Trusted profile" }.Sync(client);

        new PlanetMember(client)
        {
            Id = 1,
            PlanetId = 42,
            UserId = 9,
            User = new User(client) { Id = 9, Name = "Spoofed external profile" },
        }.Sync(client);

        Assert.True(client.Cache.Users.TryGet(9, out var cached));
        Assert.Equal("Trusted profile", cached!.Name);
    }

    [Fact]
    public void ExternalPlanetResponse_PreservesItsSourceNodeForLaterRequests()
    {
        var client = new ValourClient("https://hub.example/");
        var node = CreateExternalNode(client);
        SetNodeName(node, "community.example");
        client.NodeService.RegisterNode(node);
        client.NodeService.SetKnownByPlanet(42, node.Name);

        var planet = new Planet(client) { Id = 42 };
        var bound = (Planet)typeof(Node).GetMethod(
                "BindExternalResponseSource",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(Planet))
            .Invoke(node, [planet])!;
        bound.Sync(client);

        Assert.Equal(node.Name, bound.NodeName);
        Assert.True(client.NodeService.PlanetToNodeName.TryGetValue(bound.Id, out var nodeName));
        Assert.Equal(node.Name, nodeName);
        Assert.Same(node, bound.Node);
    }

    [Fact]
    public void ExternalPayloadForAnotherPlanet_IsRejectedBeforeItCanReachCache()
    {
        var client = new ValourClient("https://hub.example/");
        var firstNode = CreateExternalNode(client);
        var secondNode = CreateExternalNode(client);
        SetNodeName(firstNode, "first.community.example");
        SetNodeName(secondNode, "second.community.example");
        client.NodeService.RegisterNode(firstNode);
        client.NodeService.RegisterNode(secondNode);

        new Planet(client) { Id = 101, NodeName = firstNode.Name }.Sync(client);
        new Planet(client) { Id = 202, NodeName = secondNode.Name }.Sync(client);

        var exception = Assert.Throws<TargetInvocationException>(() =>
            typeof(Node).GetMethod("BindExternalResponseSource", BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(typeof(Message))
                .Invoke(firstNode, [new Message(client) { Id = 77, PlanetId = 202 }]));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.False(client.Cache.Messages.TryGet(77, firstNode.Name, out _));
        Assert.False(client.Cache.Messages.TryGet(77, secondNode.Name, out _));
    }

    [Fact]
    public void CommunityLocalModelIds_AreScopedByNodeDomain()
    {
        var client = new ValourClient("https://hub.example/");
        var firstNode = CreateExternalNode(client);
        var secondNode = CreateExternalNode(client);
        SetNodeName(firstNode, "first.community.example");
        SetNodeName(secondNode, "second.community.example");
        client.NodeService.RegisterNode(firstNode);
        client.NodeService.RegisterNode(secondNode);

        new Planet(client) { Id = 101, NodeName = firstNode.Name }.Sync(client);
        new Planet(client) { Id = 202, NodeName = secondNode.Name }.Sync(client);

        var first = new Message(client) { Id = 77, PlanetId = 101, Content = "first" }.Sync(client);
        var second = new Message(client) { Id = 77, PlanetId = 202, Content = "second" }.Sync(client);

        Assert.NotSame(first, second);
        Assert.True(client.Cache.Messages.TryGet(77, firstNode.Name, out var firstCached));
        Assert.True(client.Cache.Messages.TryGet(77, secondNode.Name, out var secondCached));
        Assert.Equal("first", firstCached!.Content);
        Assert.Equal("second", secondCached!.Content);
        Assert.False(client.Cache.Messages.TryGet(77, out _));
    }

    [Fact]
    public void CommunityPermissionNodeLookups_AreScopedByGlobalPlanetId()
    {
        var client = new ValourClient("https://hub.example/");
        var firstNode = CreateExternalNode(client);
        var secondNode = CreateExternalNode(client);
        SetNodeName(firstNode, "first.community.example");
        SetNodeName(secondNode, "second.community.example");
        client.NodeService.RegisterNode(firstNode);
        client.NodeService.RegisterNode(secondNode);

        new Planet(client) { Id = 101, NodeName = firstNode.Name }.Sync(client);
        new Planet(client) { Id = 202, NodeName = secondNode.Name }.Sync(client);

        var first = new PermissionsNode(client)
        {
            Id = 77, PlanetId = 101, TargetId = 4, RoleId = 5, TargetType = ChannelTypeEnum.PlanetChat,
        }.Sync(client);
        var second = new PermissionsNode(client)
        {
            Id = 77, PlanetId = 202, TargetId = 4, RoleId = 5, TargetType = ChannelTypeEnum.PlanetChat,
        }.Sync(client);

        Assert.NotSame(first, second);
        Assert.True(client.Cache.PermNodeKeyToId.TryGetValue(
            new PermissionsNodeKey(101, 4, 5, ChannelTypeEnum.PlanetChat), out var firstId));
        Assert.True(client.Cache.PermNodeKeyToId.TryGetValue(
            new PermissionsNodeKey(202, 4, 5, ChannelTypeEnum.PlanetChat), out var secondId));
        Assert.Equal(77, firstId);
        Assert.Equal(77, secondId);
    }

    [Fact]
    public async Task ExternalMisdirectResponse_CannotInfluenceHubRouting()
    {
        var client = new ValourClient("https://hub.example/");
        var node = CreateExternalNode(client);
        SetNodeName(node, "community.example");

        using var response = new HttpResponseMessage(HttpStatusCode.MisdirectedRequest)
        {
            Content = new StringContent("hub-internal-node:42"),
        };

        var redirected = await (Task<Node>)typeof(Node).GetMethod(
                "HandleMisdirect",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(node, [response, "GET", "api/planets/42"])!;

        Assert.Null(redirected);
        Assert.False(client.NodeService.PlanetToNodeName.ContainsKey(42));
    }

    [Fact]
    public async Task GetJsonCache_IsScopedToTheRequestOrigin()
    {
        var handler = new OriginEchoHandler();
        var provider = new HandlerProvider(handler);
        var client = new ValourClient("https://hub.example/", httpProvider: provider);
        var hubNode = new Node(client);
        await hubNode.InitializeAsync("hub");

        var externalNode = CreateExternalNode(client);
        SetNodeName(externalNode, "community.example");
        SetNodeHttpClient(externalNode, provider.GetHttpClient());

        var route = "api/cache-isolation-" + Guid.NewGuid().ToString("N");
        var external = await externalNode.GetJsonAsync<Dictionary<string, string>>(route);
        var hub = await hubNode.GetJsonAsync<Dictionary<string, string>>(route);

        Assert.True(external.Success);
        Assert.True(hub.Success);
        Assert.Equal("community.example", external.Data!["origin"]);
        Assert.Equal("hub.example", hub.Data!["origin"]);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task InviteDestination_MustBeHubSignedBeforePassportProofIsSent()
    {
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var client = new ValourClient("https://hub.example/");
        var grant = CreateInviteGrant(signingKey, "community.example");
        SetFederationJwks(client.AuthService, CreateJwks(signingKey));

        var valid = await client.AuthService.GetFederatedInviteDestinationAsync(grant);
        Assert.True(valid.Success, valid.Message);
        Assert.Equal("community.example", valid.Data);

        var tampered = ReplaceJwtPayloadString(grant, "community.example", "attacker.example");
        var rejected = await client.AuthService.GetFederatedInviteDestinationAsync(tampered);

        Assert.False(rejected.Success);
    }

    [Fact]
    public void ChangingHubToken_ClearsThePreviousAccountFederationProofMaterial()
    {
        var client = new ValourClient("https://hub.example/");
        client.AuthService.SetToken("first-account-token");
        var node = CreateExternalNode(client);
        SetNodeName(node, "community.example");
        client.NodeService.RegisterNode(node);
        client.NodeService.SetKnownByPlanet(42, node.Name);
        SetFederationJwks(client.AuthService, "{\"keys\":[]}");
        typeof(AuthService).GetField("_federationPassport", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client.AuthService, new FederationPassportResponse
            {
                Token = "previous-account-passport",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            });

        client.AuthService.SetToken("second-account-token");

        Assert.Null(client.AuthService.ExportFederationPassportCache());
        Assert.Null(typeof(AuthService).GetField("_federationHubJwks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client.AuthService));
        Assert.False(client.NodeService.NameToNode.ContainsKey(node.Name));
        Assert.False(client.NodeService.PlanetToNodeName.ContainsKey(42));
        Assert.True(node.NeedsFederationSessionRefresh);
    }

    private static Node CreateExternalNode(ValourClient client)
    {
        var node = new Node(client);
        typeof(ServiceBase).GetMethod("SetupLogging", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(node, [client.Logger, new LogOptions("test")]);
        typeof(Node).GetField("_externalBaseUrl", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(node, "https://community.example/");
        typeof(Node).GetField("_externalTokenExpiresAt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(node, DateTime.UtcNow.AddMinutes(10));
        return node;
    }

    private static void SetNodeName(Node node, string name) =>
        typeof(Node).GetField("<Name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(node, name);

    private static void SetNodeHttpClient(Node node, HttpClient httpClient) =>
        typeof(Node).GetField("<HttpClient>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(node, httpClient);

    private static void SetFederationJwks(AuthService authService, string jwks)
    {
        typeof(AuthService).GetField("_federationHubJwks", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(authService, jwks);
        typeof(AuthService).GetField("_federationHubJwksFetchedAt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(authService, DateTime.UtcNow);
    }

    private static void InvokeUnauthorizedResponse(Node node) =>
        typeof(Node).GetMethod("NotifyIfUnauthorized", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(node, [new HttpResponseMessage(HttpStatusCode.Unauthorized)]);

    private static bool AcceptsPlanetEvent(Node node, long? planetId) =>
        (bool)typeof(Node).GetMethod(
                "AcceptsExternalPlanetRealtimeEvent",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(node, [planetId])!;

    private static string CreateInviteGrant(ECDsa key, string domain)
    {
        var securityKey = new ECDsaSecurityKey(key) { KeyId = "hub-test-key" };
        var descriptor = new SecurityTokenDescriptor
        {
            Audience = domain,
            Expires = DateTime.UtcNow.AddMinutes(5),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["purpose"] = ValourFederation.InvitePurpose,
                ["protocol"] = ValourFederation.ProtocolVersion,
                ["node_domain"] = domain,
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string CreateJwks(ECDsa key)
    {
        var parameters = key.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC",
                    crv = "P-256",
                    x = Base64UrlEncode(parameters.Q.X!),
                    y = Base64UrlEncode(parameters.Q.Y!),
                    kid = "hub-test-key",
                    alg = "ES256",
                    use = "sig",
                },
            },
        });
    }

    private static string ReplaceJwtPayloadString(string token, string from, string to)
    {
        var parts = token.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1])).Replace(from, to, StringComparison.Ordinal);
        return parts[0] + "." + Base64UrlEncode(Encoding.UTF8.GetBytes(payload)) + "." + parts[2];
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    private sealed class HandlerProvider(HttpMessageHandler handler) : HttpClientProvider
    {
        public HttpClient GetHttpClient() => new(handler);

        public HttpMessageHandler GetHttpMessageHandler() => handler;
    }

    private sealed class OriginEchoHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var origin = request.RequestUri!.Host;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"origin\":\"{origin}\"}}"),
            });
        }
    }
}
