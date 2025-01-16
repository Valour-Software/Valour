using Valour.Sdk.Client;
using Valour.Server;

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

    public async Task InitializeAsync()
    {
        // Create the underlying WebApplicationFactory
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
            });

        // Create a client from the factory
        var httpClient = Factory.CreateClient();
        
        
        
        Client = new ValourClient("https://localhost:5001/");
        httpClient.BaseAddress = new Uri(Client.BaseAddress);
        Client.SetHttpClient(httpClient);

        // Sets up the primary node
        await Client.NodeService.SetupPrimaryNodeAsync();
        
        Console.WriteLine("Initialized LoginTestFixture");
    }

    public Task DisposeAsync()
    {
        // Clean up if needed
        Factory.Dispose();
        return Task.CompletedTask;
    }
}
