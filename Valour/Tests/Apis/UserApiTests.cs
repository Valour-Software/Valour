using Microsoft.AspNetCore.Mvc.Testing;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Shared.Models;
using Xunit.Extensions.Ordering;

namespace Valour.Tests.Apis;

[Order(-1)]
//public class UserApiTests : IClassFixture<WebApplicationFactory<Program>>
public class UserApiTests : IClassFixture<LoginTestFixture>
{
    private readonly HttpClient _httpClient;
    private readonly ValourClient _client;

    public UserApiTests(LoginTestFixture fixture)
    {
        _client = fixture.Client;
        _httpClient = _client.Http;
    }
    
    [Fact]
    [Order(0)]
    public async Task TestGetUserCount()
    {
        var response = await _httpClient.GetAsync("api/users/count");
        response.EnsureSuccessStatusCode();
        
        // Ensure response is a number
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(int.TryParse(content, out _));
    }
    
    [Fact]
    [Order(-5)]
    public async Task TestRegisterUser()
    {
        var testString = Guid.NewGuid().ToString().Substring(0, 8);

        var testEmail = $"test-{testString}@test.xyz";
        var testUsername = $"test-{testString}";
        var testPassword = $"Test-{testString}";
        
        var result = await _client.AuthService.RegisterAsync(new RegisterUserRequest()
        {
            Email = testEmail,
            Locality = Locality.General,
            Password = testPassword,
            Username = testUsername,
            DateOfBirth = new DateTime(2000, 1, 1),
            Source = "test"
        });
        
        Assert.NotNull(result);
        Assert.True(result.Success);
    }
}