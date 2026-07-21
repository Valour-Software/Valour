using System.Net;
using System.Net.Http.Json;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Sdk.Nodes;
using Valour.Sdk.Requests;
using Valour.Sdk.Utility;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class ChannelMutationSyncTests
{
    [Fact]
    public async Task CreatePlanetChannelAsync_CachesHttpResponseWithoutRealtimeEvent()
    {
        const long planetId = 42;
        const long channelId = 84;
        var provider = new StubHttpClientProvider(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ChannelDto
                {
                    Id = channelId,
                    PlanetId = planetId,
                    Name = "created",
                    Description = "created channel",
                    ChannelType = ChannelTypeEnum.PlanetChat,
                    RawPosition = 1
                })
            };
        });
        var (client, planet) = await CreateClientAsync(planetId, provider);
        var request = new CreateChannelRequest
        {
            Channel = new Channel(client)
            {
                PlanetId = planetId,
                Name = "created",
                Description = "created channel",
                ChannelType = ChannelTypeEnum.PlanetChat
            },
            Nodes = []
        };

        var result = await client.ChannelService.CreatePlanetChannelAsync(planet, request);

        Assert.True(result.Success, result.Message);
        Assert.True(planet.Channels.TryGet(channelId, out var planetChannel));
        Assert.True(client.Cache.Channels.TryGet(channelId, out var globalChannel));
        Assert.Same(planetChannel, result.Data);
        Assert.Same(planetChannel, globalChannel);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSuccessfulChannelWithoutRealtimeEvent()
    {
        const long planetId = 43;
        const long channelId = 86;
        var provider = new StubHttpClientProvider(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var (client, _) = await CreateClientAsync(planetId, provider);
        var channel = new Channel(client)
        {
            Id = channelId,
            PlanetId = planetId,
            Name = "delete me",
            Description = "delete me",
            ChannelType = ChannelTypeEnum.PlanetChat,
            RawPosition = 1
        }.Sync(client);

        var result = await channel.DeleteAsync();

        Assert.True(result.Success, result.Message);
        Assert.False(channel.Planet.Channels.ContainsId(channelId));
        Assert.False(client.Cache.Channels.ContainsId(channelId));
    }

    [Fact]
    public async Task MoveChannelAsync_RefreshesTreeWithoutRealtimeEvent()
    {
        const long planetId = 44;
        const long categoryId = 88;
        const long channelId = 89;
        var categoryPosition = ChannelPosition.AppendRelativePosition(0, 1);
        var childPosition = ChannelPosition.AppendRelativePosition(categoryPosition, 1);
        var requests = new List<HttpMethod>();
        var provider = new StubHttpClientProvider(request =>
        {
            requests.Add(request.Method);
            if (request.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            Assert.Equal(HttpMethod.Get, request.Method);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new ChannelDto
                    {
                        Id = categoryId,
                        PlanetId = planetId,
                        Name = "Category",
                        Description = "Category",
                        ChannelType = ChannelTypeEnum.PlanetCategory,
                        RawPosition = categoryPosition
                    },
                    new ChannelDto
                    {
                        Id = channelId,
                        PlanetId = planetId,
                        ParentId = categoryId,
                        Name = "Moved",
                        Description = "Moved",
                        ChannelType = ChannelTypeEnum.PlanetChat,
                        RawPosition = childPosition
                    }
                })
            };
        });
        var (client, planet) = await CreateClientAsync(planetId, provider);
        var category = new Channel(client)
        {
            Id = categoryId,
            PlanetId = planetId,
            Name = "Category",
            Description = "Category",
            ChannelType = ChannelTypeEnum.PlanetCategory,
            RawPosition = categoryPosition
        }.Sync(client);
        var channel = new Channel(client)
        {
            Id = channelId,
            PlanetId = planetId,
            Name = "Moved",
            Description = "Moved",
            ChannelType = ChannelTypeEnum.PlanetChat,
            RawPosition = 2
        }.Sync(client);

        var result = await client.ChannelService.MoveChannelAsync(
            channel,
            category,
            placeBefore: false,
            insideCategory: true);

        Assert.True(result.Success, result.Message);
        Assert.Equal([HttpMethod.Post, HttpMethod.Get], requests);
        Assert.True(planet.Channels.TryGet(channelId, out var moved));
        Assert.Equal(categoryId, moved.ParentId);
        Assert.Equal(
            category.RawPosition,
            new ChannelPosition(moved.RawPosition).GetParentPosition().RawPosition);
    }

    private static async Task<(ValourClient Client, Planet Planet)> CreateClientAsync(
        long planetId,
        HttpClientProvider provider)
    {
        var client = new ValourClient("https://valour.test/", httpProvider: provider);
        var node = new Node(client);
        var nodeResult = await node.InitializeAsync("test", isPrimary: true);
        Assert.True(nodeResult.Success, nodeResult.Message);
        client.PrimaryNode = node;
        client.NodeService.SetKnownByPlanet(planetId, "test");

        var planet = new Planet(client)
        {
            Id = planetId,
            Name = "Test Planet",
            Description = "Test Planet"
        }.Sync(client);
        planet.SetNode(node);

        return (client, planet);
    }

    private sealed class StubHttpClientProvider(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpClientProvider
    {
        private readonly StubHandler _handler = new(responder);

        public HttpClient GetHttpClient() => new(_handler, disposeHandler: false);

        public HttpMessageHandler GetHttpMessageHandler() => _handler;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }

    private sealed class ChannelDto
    {
        public long Id { get; set; }
        public long? PlanetId { get; set; }
        public long? ParentId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ChannelTypeEnum ChannelType { get; set; }
        public uint RawPosition { get; set; }
    }
}
