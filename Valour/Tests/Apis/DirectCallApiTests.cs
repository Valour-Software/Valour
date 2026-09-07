using System.Net;

namespace Valour.Tests.Apis;

[Collection("ApiCollection")]
public class DirectCallApiTests
{
    private readonly LoginTestFixture _fixture;

    public DirectCallApiTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CurrentCallRoute_RejectsAnonymousRequests()
    {
        using var anonymous = _fixture.Factory.CreateClient();

        using var response = await anonymous.GetAsync("api/direct-calls/current");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CurrentCallRoute_ReturnsAuthenticatedUsersCalls()
    {
        using var response = await _fixture.Client.Http.GetAsync("api/direct-calls/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
