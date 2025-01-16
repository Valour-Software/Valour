using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.SwaggerUi;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Services;
using Valour.Shared.Models;
using Xunit.Extensions.Ordering;

namespace Valour.Tests;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public class LoginTestFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public ValourClient Client { get; private set; } = null!;
    public  RegisterUserRequest TestUserDetails { get; private set; } = null!;


    public async Task InitializeAsync()
    {
        // Create the underlying WebApplicationFactory
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("https_port", "5001");
                builder.UseUrls("http://localhost:5000");
            });

        // Create a client from the factory
        var httpClient = Factory.CreateClient();
        
        Client = new ValourClient("https://localhost:5001/", httpProvider: new TestHttpProvider(Factory));
        httpClient.BaseAddress = new Uri(Client.BaseAddress);
        Client.SetHttpClient(httpClient);

        // Sets up the primary node
        await Client.NodeService.SetupPrimaryNodeAsync();

        // Register a user
        await RegisterUser();
        
        // Log in the user
        await TestLoginUser();
        
        Console.WriteLine("Initialized LoginTestFixture");
    }
    
    public async Task RegisterUser()
    {
        var testString = Guid.NewGuid().ToString().Substring(0, 8);

        var testEmail = $"test-{testString}@test.xyz";
        var testUsername = $"test-{testString}";
        var testPassword = $"Test-{testString}";

        TestUserDetails = new RegisterUserRequest()
        {
            Email = testEmail,
            Locality = Locality.General,
            Password = testPassword,
            Username = testUsername,
            DateOfBirth = new DateTime(2000, 1, 1),
            Source = "test"
        };

        try
        {
            var result = await Client.AuthService.RegisterAsync(TestUserDetails);

            Assert.NotNull(result);
            Assert.True(result.Success);

            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var dbUser = await db.Users.Where(x => x.Name == testUsername).FirstOrDefaultAsync();

            Assert.NotNull(dbUser);

            var emailConfirmCode = await db.EmailConfirmCodes.FirstOrDefaultAsync(x => x.UserId == dbUser.Id);

            Assert.NotNull(emailConfirmCode);

            // GET to confirm email
            var response = await Client.Http.GetAsync($"/api/users/verify/{emailConfirmCode.Code}");
            response.EnsureSuccessStatusCode();

            Console.WriteLine("Registered Test User");
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to register Test User");
            Console.WriteLine(e);
            throw;
        }
    }
    
    public async Task TestLoginUser()
    {
        var oldClient = Client;

        try
        {
            // Build new client for logged in user
            Client = new ValourClient("https://localhost:5001/", httpProvider: oldClient.HttpClientProvider);
            Client.SetHttpClient(oldClient.Http);

            var loginResult = await Client.AuthService.LoginAsync(TestUserDetails.Email, TestUserDetails.Password);

            Assert.NotNull(loginResult);
            Assert.True(loginResult.Success);

            Console.WriteLine("Logged in to Test User");
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to log in to Test User");
            Console.WriteLine(e);
            throw;
        }
    }

    public Task DisposeAsync()
    {
        // Clean up if needed
        Factory.Dispose();
        return Task.CompletedTask;
    }
}
