using Valour.Sdk.Nodes;

namespace Valour.Tests.Client;

public class NodeHeartbeatTests
{
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
