using System.Net;
using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Sdk.Utility;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class SentryTransportTests
{
    [Theory]
    [InlineData("bad\ntoken")]
    [InlineData("bad\rtoken")]
    [InlineData("bad\0token")]
    public async Task InvalidSavedCredential_ReturnsFailureBeforeRegisteringNode(string token)
    {
        var client = new ValourClient("https://api.valour.example/");
        client.AuthService.SetToken(token);
        var result = await new Node(client).InitializeAsync("primary");
        Assert.False(result.Success);
        Assert.DoesNotContain(token, result.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginTransportFailure_ReturnsFailureWithoutChangingCredential(bool native)
    {
        var provider = new StubProvider(_ => throw (native
            ? new WebException("Offline") : new HttpRequestException("Offline")));
        var client = new ValourClient("https://api.valour.example/", httpProvider: provider);
        var http = provider.GetHttpClient();
        http.BaseAddress = new Uri(client.BaseAddress);
        client.SetHttpClient(http);
        var result = await client.AuthService.FetchToken("test@example.test", "unused");
        Assert.False(result.Success);
        Assert.Null(client.AuthService.Token);
    }

    [Fact]
    public async Task ResendConfirmation_UsesUnauthenticatedClientWithoutPrimaryNode()
    {
        string? path = null;
        var provider = new StubProvider(request =>
        {
            path = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        });
        var client = new ValourClient("https://api.valour.example/", httpProvider: provider);
        var http = provider.GetHttpClient();
        http.BaseAddress = new Uri(client.BaseAddress);
        client.SetHttpClient(http);
        var result = await client.AuthService.ResendConfirmationEmailAsync(new RegisterUserRequest { Email = "test@example.test" });
        Assert.True(result.Success, result.Message);
        Assert.Equal("/api/users/resendemail", path);
        Assert.Null(client.PrimaryNode);
    }

    private sealed class StubProvider(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpClientProvider
    {
        private readonly StubHandler _handler = new(responder);
        public HttpClient GetHttpClient() => new(_handler, false);
        public HttpMessageHandler GetHttpMessageHandler() => _handler;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
