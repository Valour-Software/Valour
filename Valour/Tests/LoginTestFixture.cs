using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Utilities;
using Valour.Server.Workers;
using Valour.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Valour.Tests;

public class TestShared
{
    public static RegisterUserRequest PrimaryTestUserDetails { get; set; }
}

/// <summary>
/// The test server shared by every fixture in a run. Starting a server applies
/// migrations, connects to Redis, and starts every hosted worker, which takes
/// several seconds, so fixtures share one instead of starting their own. Each
/// fixture still registers its own user. The server starts the first time a
/// fixture asks for it, so runs that use no fixture never reach the database.
/// </summary>
public static class SharedTestServer
{
    private static readonly Lazy<WebApplicationFactory<Program>> LazyFactory = new(Create);

    public static WebApplicationFactory<Program> Factory => LazyFactory.Value;

    private static WebApplicationFactory<Program> Create()
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("https_port", "5001");
                builder.UseSetting(RateLimitPolicies.DisabledSetting, "true");
                // Tests wait for staged messages to be stored, so flush often
                builder.UseSetting(PlanetMessageWorker.FlushIntervalSetting, "00:00:00.200");
                builder.UseUrls("http://localhost:5000");
            });

        // Start the server here so concurrent first uses start it only once
        _ = factory.Server;
        return factory;
    }
}

public class LoginTestFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public ValourClient Client { get; private set; } = null!;
    public  RegisterUserRequest PrimaryTestUserDetails { get; private set; } = null!;
    
    public bool UserRegistered { get; private set; } = false;
    public bool UserLoggedIn { get; private set; } = false;
    
    public async ValueTask InitializeAsync()
    {
        Factory = SharedTestServer.Factory;

        // Create a client from the factory
        var httpClient = Factory.CreateClient();
        
        Client = new ValourClient("https://localhost:5001/", httpProvider: new TestHttpProvider(Factory));
        Client.E2eeService.KeyStore = new Valour.Sdk.E2ee.MemoryE2eeKeyStore();
        httpClient.BaseAddress = new Uri(Client.BaseAddress);
        Client.SetHttpClient(httpClient);
        
        // Sets up the primary node
        await Client.NodeService.SetupPrimaryNodeAsync();

        // Register a user
        await RegisterUser(true);
        
        // Log in the user
        await TestLoginUser();
        
        Console.WriteLine("Initialized LoginTestFixture");
    }
    
    public async Task<RegisterUserRequest> RegisterUser(bool primary = false)
    {
        var testString = Guid.NewGuid().ToString().Substring(0, 8);

        var testEmail = $"test-{testString}@test.xyz";
        var testUsername = $"test-{testString}";
        var testPassword = $"Test-{testString}";

        var details = new RegisterUserRequest()
        {
            Email = testEmail,
            Password = testPassword,
            Username = testUsername,
            DateOfBirth = new DateTime(2000, 1, 1),
            Source = "test",
            IsNotTexasResident = true
        };
        
        if (primary)
        {
            PrimaryTestUserDetails = details;
            TestShared.PrimaryTestUserDetails = details;
        }

        try
        {
            var result = await Client.AuthService.RegisterAsync(details);

            Assert.NotNull(result);
            Assert.True(result.Success);

            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var dbUser = await db.Users.Where(x => x.Name == testUsername).FirstOrDefaultAsync();

            Assert.NotNull(dbUser);

            var emailConfirmCode = await db.EmailConfirmCodes.FirstOrDefaultAsync(x => x.UserId == dbUser.Id);

            // If email isn't set up, there won't be a confirm code
            if (emailConfirmCode is not null)
            {
                // GET to confirm email
                var response = await Client.Http.GetAsync($"/api/users/verify/{emailConfirmCode.Code}");
                response.EnsureSuccessStatusCode();
            }

            Console.WriteLine("Startup: Registered Test User");
            
            UserRegistered = true;

            return details;
        }
        catch (Exception e)
        {
            Console.WriteLine("Startup: Failed to register Test User");
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

            // Messages are always end-to-end encrypted, so the test user gets
            // keys the way an app does at its first login.
            Client.E2eeService.KeyStore = new Valour.Sdk.E2ee.MemoryE2eeKeyStore();

            var loginResult = await Client.AuthService.LoginAsync(PrimaryTestUserDetails.Email, PrimaryTestUserDetails.Password);

            Assert.NotNull(loginResult);
            Assert.True(loginResult.Success);

            await Client.E2eeService.InitializeAsync();

            Console.WriteLine("Startup: Logged in to Test User");
            
            UserLoggedIn = true;
        }
        catch (Exception e)
        {
            Console.WriteLine("Startup: Failed to log in to Test User");
            Console.WriteLine(e);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        /*
        // Hard delete the test user
        try
        {
            await Client.DeleteMyAccountAsync(TestUserDetails.Password);
            Console.WriteLine("Deleted Test User");
            
            UserDeleted = true;
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to delete Test User");
            Console.WriteLine(e);

            throw;
        }
        */

        // The shared server outlives this fixture and stops when the run ends
        return ValueTask.CompletedTask;
    }
}

// Similar to login, but doesn't register. Just uses shared user details to login
public class TeardownTestFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public ValourClient Client { get; private set; } = null!;
    public RegisterUserRequest TestUserDetails { get; private set; } = null!;
    
    public async ValueTask InitializeAsync()
    {
        Factory = SharedTestServer.Factory;
        
        // Log in the user
        await TestLoginUser();
        
        Console.WriteLine("Initialized TeardownTestFixture");
    }
    
    public async Task TestLoginUser()
    {
        try
        {
            // Create a client from the factory
            var httpClient = Factory.CreateClient();
            
            // Build new client for logged in user
            Client = new ValourClient("https://localhost:5001/", httpProvider: new TestHttpProvider(Factory));
            Client.SetHttpClient(httpClient);
            Client.E2eeService.KeyStore = new Valour.Sdk.E2ee.MemoryE2eeKeyStore();

            var loginResult = await Client.AuthService.LoginAsync(TestShared.PrimaryTestUserDetails.Email, TestShared.PrimaryTestUserDetails.Password);

            Assert.NotNull(loginResult);
            Assert.True(loginResult.Success);

            Console.WriteLine("Teardown: Logged in to Test User");
        }
        catch (Exception e)
        {
            Console.WriteLine("Teardown: Failed to log in to Test User");
            Console.WriteLine(e);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        // The shared server outlives this fixture and stops when the run ends
        return ValueTask.CompletedTask;
    }
}
