using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Nodes;
using Valour.Server.Hubs;

namespace Valour.Tests.Client;

[Collection("ApiCollection")]
public class NodeHeartbeatHubTests(LoginTestFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Heartbeat_SendsRequiredArgumentToCoreHub(bool userState)
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl($"http://localhost{CoreHub.HubUrl}", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => fixture.Factory.Server.CreateHandler();
            }).Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await connection.StartAsync(timeout.Token);
        Assert.Equal("pong", await Node.InvokeHeartbeatAsync(connection, userState, timeout.Token));
    }
}
