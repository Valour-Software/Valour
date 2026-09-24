using System.Net;
using Google.Authenticator;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Shared;

namespace Valour.Tests.Apis;

[Collection("ApiCollection")]
public class OpenIssueApiRegressionTests(LoginTestFixture fixture)
{
    [Fact]
    public async Task Mfa_CanSetUpVerifyRemoveAndSetUpAgain()
    {
        var auth = fixture.Client.AuthService;
        var node = fixture.Client.PrimaryNode;
        Assert.True((await node.SetupRealtimeConnection()).Success);
        var originalToken = auth.Token;
        var setup = await auth.SetupMfaAsync();
        Assert.True(setup.Success, setup.Message);
        Assert.False(string.IsNullOrWhiteSpace(setup.Data.Key));
        Assert.False(string.IsNullOrWhiteSpace(setup.Data.QRCode));

        var code = new TwoFactorAuthenticator().GetCurrentPIN(setup.Data.Key, true);
        var verified = await auth.VerifyMfaAsync(code);
        Assert.True(verified.Success, verified.Message);
        try
        {
            var duplicate = await auth.SetupMfaAsync();
            Assert.False(duplicate.Success);
        }
        finally
        {
            var removed = await auth.RemoveMfaAsync(fixture.PrimaryTestUserDetails.Password);
            Assert.True(removed.Success, removed.Message);
        }

        Assert.NotEqual(originalToken, auth.Token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var joined = await node.HubConnection.InvokeAsync<TaskResult>("JoinUser", true, timeout.Token);
        Assert.True(joined.Success, joined.Message);

        var replacement = await auth.SetupMfaAsync();
        Assert.True(replacement.Success, replacement.Message);
        Assert.NotEqual(setup.Data.Key, replacement.Data.Key);
        var cleanup = await auth.RemoveMfaAsync(fixture.PrimaryTestUserDetails.Password);
        Assert.True(cleanup.Success, cleanup.Message);
    }

    [Theory]
    [InlineData("https://0.0.0.0", true)]
    [InlineData("https://0.0.0.1", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task NativeUploads_PreflightAllowsOnlyTrustedOrigins(string origin, bool allowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "upload/file");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        using var response = await fixture.Client.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }
}
