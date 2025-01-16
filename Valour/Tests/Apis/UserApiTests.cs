using Valour.Sdk.Client;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

[Collection("ApiCollection")]
public class UserApiTests
{
    private readonly HttpClient _httpClient;
    private ValourClient _client;
    private RegisterUserRequest _testUserDetails;
    private LoginTestFixture _fixture;

    public UserApiTests(LoginTestFixture fixture)
    {
        _client = fixture.Client;
        _httpClient = _client.Http;
        _fixture = fixture;
    }

    [Fact]
    public void UserRegistered()
    {
        Assert.True(_fixture.UserRegistered);
    }
    
    [Fact]
    public void UserLoggedIn()
    {
        Assert.True(_fixture.UserLoggedIn);
    }
    
    [Fact]
    public async Task TestGetUserCount()
    {
        var response = await _httpClient.GetAsync("api/users/count");
        response.EnsureSuccessStatusCode();
        
        // Ensure response is a number
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(int.TryParse(content, out _));
    }
    
    [Fact]
    public async Task TestGetUser()
    {
        var user = await _client.UserService.FetchUserAsync(_client.Me.Id);
        Assert.NotNull(user);
        Assert.Equal(user.Id, _client.Me.Id);
    }
}