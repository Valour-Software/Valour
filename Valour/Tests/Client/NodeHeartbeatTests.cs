using Microsoft.AspNetCore.SignalR.Client;
using Valour.Sdk.Nodes;

namespace Valour.Tests.Client;

public class NodeHeartbeatTests
{
    [Theory]
    [InlineData("JoinChannel")]
    [InlineData("LeaveChannel")]
    [InlineData("JoinUser")]
    public async Task RealtimeRequestAfterDisconnect_ReturnsFailure(string method)
    {
        await using var connection = new Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder()
            .WithUrl("http://localhost:1/hubs/core")
            .Build();
        var result = await Node.InvokeRealtimeAsync(connection, method, 1L);
        Assert.False(result.Success);
        Assert.Contains("not active", result.Message);
    }

    [Fact]
    public void ShouldForceReconnect_LocalHeartbeatTimeout_ReturnsTrue()
    {
        var exception = new Node.HeartbeatTimeoutException("SignalR ping timed out.");

        Assert.True(Node.ShouldForceReconnectForHeartbeatException(exception));
    }

    [Fact]
    public void ShouldForceReconnect_UnrelatedTimeout_ReturnsFalse()
    {
        Assert.False(Node.ShouldForceReconnectForHeartbeatException(new TimeoutException()));
    }

    [Fact]
    public void ShouldForceReconnect_HubPingInvocationFailure_ReturnsFalse()
    {
        var exception = new InvalidOperationException("Failed to invoke 'Ping' due to an error on the server.");

        Assert.False(Node.ShouldForceReconnectForHeartbeatException(exception));
    }
}
