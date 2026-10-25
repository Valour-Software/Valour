using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Services;
using Valour.Shared.Models;
using Valour.Shared;

namespace Valour.Tests.Compatibility;

/// <summary>
/// The server under test and the clients that talk to it. The server runs in
/// its own process at the address in VALOUR_COMPAT_SERVER, usually started by
/// Tools/Compatibility/run-client-tests.py, so these tests reach it only over
/// HTTP and SignalR, as an installed app does.
/// </summary>
public static class CompatibilityServer
{
    public const string AddressVariable = "VALOUR_COMPAT_SERVER";

    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(20);

    public static string Address => Environment.GetEnvironmentVariable(AddressVariable);

    /// <summary>Skips the calling test when no server address is configured.</summary>
    public static void Require() =>
        Assert.SkipWhen(string.IsNullOrWhiteSpace(Address),
            $"Set {AddressVariable} to the address of a running test server, or run " +
            "Tools/Compatibility/run-client-tests.py. See Docs/ClientCompatibility.md.");

    /// <summary>
    /// Registers a new account and signs in with a fresh device, which sets up
    /// end-to-end encryption the way an app does on first sign-in.
    /// </summary>
    public static async Task<ValourClient> CreateUserAsync()
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var details = new RegisterUserRequest
        {
            Email = $"compat-{id}@test.xyz",
            Password = $"Compat-{id}-pass",
            Username = $"compat-{id}",
            DateOfBirth = new DateTime(2000, 1, 1),
            Source = "compatibility",
            IsNotTexasResident = true,
        };

        var registrar = NewClient();
        var registered = await registrar.AuthService.RegisterAsync(details);
        Assert.True(registered.Success, $"Registration failed: {registered.Message}");

        var client = NewClient();
        client.E2eeService.KeyStore = new MemoryE2eeKeyStore();
        client.E2eeService.AutoSetUp = true;
        client.E2eeService.AutoRecover = false;
        client.E2eeService.StoreRecoveryCode = false;
        client.E2eeService.DeviceName = "Compatibility test";

        var login = await client.AuthService.LoginAsync(details.Email, details.Password);
        Assert.True(login.Success, $"Sign-in failed: {login.Message}");

        await client.E2eeService.InitializeAsync();
        Assert.Equal(E2eeStatus.Ready, client.E2eeService.Status);
        return client;
    }

    /// <summary>
    /// A client set up the way the apps set up theirs, with its own HTTP client:
    /// signing in sets a default authorization header, which must not reach
    /// another client.
    /// </summary>
    private static ValourClient NewClient()
    {
        var client = new ValourClient(Address);
        client.SetHttpClient(new HttpClient { BaseAddress = new Uri(client.BaseAddress) });
        return client;
    }

    /// <summary>
    /// Creates a public planet whose members get its channel keys from members
    /// who hold them, and returns it with its primary chat channel.
    /// </summary>
    public static async Task<(Planet Planet, Channel Channel)> CreatePlanetAsync(ValourClient owner)
    {
        var created = await new Planet(owner)
        {
            Name = $"Compat {Guid.NewGuid().ToString("N")[..8]}",
            Description = "Client compatibility test planet",
            Public = true,
            Discoverable = true,
        }.CreateAsync();
        Assert.True(created.Success, $"Planet creation failed: {created.Message}");

        var planet = await owner.PlanetService.FetchPlanetAsync(created.Data.Id, skipCache: true);
        await planet.EnsureReadyAsync();

        var privacy = await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: true, sharesHistory: true);
        Assert.True(privacy.Success, $"Setting planet privacy failed: {privacy.Message}");

        return (planet, await planet.FetchPrimaryChatChannelAsync());
    }

    /// <summary>
    /// Joins <paramref name="member"/> to the owner's planet, gives it the
    /// channel's keys, and opens the planet and channel for realtime updates as
    /// the app does when someone views the channel.
    /// </summary>
    public static async Task<(Planet Planet, Channel Channel)> JoinAndOpenAsync(
        ValourClient owner, Channel ownerChannel, ValourClient member)
    {
        var joined = await member.PlanetService.JoinPlanetAsync(ownerChannel.PlanetId!.Value);
        Assert.True(joined.Success, $"Joining the planet failed: {joined.Message}");

        var planet = await member.PlanetService.FetchPlanetAsync(ownerChannel.PlanetId!.Value, skipCache: true);
        await planet.EnsureReadyAsync();
        var channel = await planet.FetchChannelAsync(ownerChannel.Id);
        Assert.NotNull(channel);

        await member.E2eeService.RequestKeysAsync(channel);
        await owner.E2eeService.ServeChannelAsync(ownerChannel);
        var ring = await member.E2eeService.GetKeyRingAsync(channel, refresh: true);
        Assert.NotNull(ring?.Latest);

        var planetOpened = await member.PlanetService.TryOpenPlanetConnection(planet, "compatibility");
        Assert.True(planetOpened.Success, $"Opening the planet failed: {planetOpened.Message}");
        var channelOpened = await channel.OpenWithResult("compatibility");
        Assert.True(channelOpened.Success, $"Opening the channel failed: {channelOpened.Message}");

        return (planet, channel);
    }

    public static Task<TaskResult<Message>> SendAsync(ValourClient client, Channel channel, string content,
        long? memberId = null) =>
        client.MessageService.SendMessage(new Message(client)
        {
            Content = content,
            ChannelId = channel.Id,
            PlanetId = channel.PlanetId,
            AuthorUserId = client.Me.Id,
            AuthorMemberId = memberId,
            Fingerprint = Guid.NewGuid().ToString(),
        });

    /// <summary>
    /// Runs <paramref name="action"/> and waits for the first event that
    /// matches, failing with <paramref name="description"/> if none arrives.
    /// The handler is attached before the action runs, so an event the server
    /// sends while the action is still returning is not missed.
    /// </summary>
    public static async Task<T> NextAsync<T>(
        Action<Action<T>> subscribe, Action<Action<T>> unsubscribe,
        Func<T, bool> match, Func<Task> action, string description)
    {
        var arrived = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<T> handler = value =>
        {
            if (match(value))
                arrived.TrySetResult(value);
        };

        subscribe(handler);
        try
        {
            await action();
            return await arrived.Task.WaitAsync(EventTimeout);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"No realtime event arrived within {EventTimeout.TotalSeconds} seconds: {description}.");
            throw;
        }
        finally
        {
            unsubscribe(handler);
        }
    }
}
