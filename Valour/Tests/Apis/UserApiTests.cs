using Microsoft.AspNetCore.Mvc.Testing;
using Valour.Server;

namespace Valour.Tests.Apis;

public class UserApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public UserApiTests(WebApplicationFactory<Program> fixture)
    {
        _client = fixture.CreateClient();
    }
    
    [Fact]
    public async Task TestGetUserCount()
    {
        var response = await _client.GetAsync("api/users/count");
        response.EnsureSuccessStatusCode();
        
        // Ensure response is a number
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(int.TryParse(content, out _));
    }
}