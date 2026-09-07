# Bot development

A Valour bot uses an account token and the same API and permission system as the
client. The SDK handles authentication, planet data, HTTP requests, and real-time
subscriptions. This guide uses a source reference so the example runs against the
SDK in this checkout.

## Create an account and token

Sign into Valour with your user account, open the Developer settings, and create
a bot with a name. Copy the token when it is shown and store it privately. Token
regeneration invalidates the token it replaces. The bot management service limits
an owner to ten bots.

Add the bot to a planet through an invite using the SDK's
`PlanetService.JoinPlanetAsync(planetId, inviteCode)`. Check the returned result
before proceeding. The bot needs channel access and permission to send messages;
automation does not bypass the planet's roles.

## Create a project

Use the .NET SDK pinned by the repository's `global.json`. From a directory for
your bot project:

```sh
dotnet new console -n MyValourBot --framework net11.0
dotnet add MyValourBot/MyValourBot.csproj reference /path/to/Valour/Valour/Sdk/Valour.Sdk.csproj
```

Set `VALOUR_BOT_TOKEN`, `VALOUR_BOT_PLANET_ID`, and `VALOUR_BOT_CHANNEL_ID` in your
process environment. The planet and channel must already be accessible to the bot.
An optional `VALOUR_BOT_URL` selects an instance and defaults to the official app.
Keep the token out of source files, command logs, and screenshots.

## A channel bot

Replace `Program.cs` with this example. It responds to `!hello` in one selected
channel and removes its subscription when Ctrl+C stops the process.

```csharp
using Valour.Sdk.Client;
using Valour.Sdk.Models;

var token = Environment.GetEnvironmentVariable("VALOUR_BOT_TOKEN")
    ?? throw new InvalidOperationException("Set VALOUR_BOT_TOKEN.");
var planetId = long.Parse(Environment.GetEnvironmentVariable("VALOUR_BOT_PLANET_ID")
    ?? throw new InvalidOperationException("Set VALOUR_BOT_PLANET_ID."));
var channelId = long.Parse(Environment.GetEnvironmentVariable("VALOUR_BOT_CHANNEL_ID")
    ?? throw new InvalidOperationException("Set VALOUR_BOT_CHANNEL_ID."));
var url = Environment.GetEnvironmentVariable("VALOUR_BOT_URL")
    ?? "https://app.valour.gg/";

var client = new ValourClient(url);
client.SetupHttpClient();
var login = await client.InitializeUser(token);
if (!login.Success)
    throw new InvalidOperationException(login.Message);

var planet = client.PlanetService.JoinedPlanets.FirstOrDefault(p => p.Id == planetId)
    ?? throw new InvalidOperationException("The bot has not joined this planet.");
await planet.EnsureReadyAsync();
var initial = await planet.FetchInitialDataAsync();
if (!initial.Success)
    throw new InvalidOperationException(initial.Message);
var connection = await planet.ConnectToRealtime();
if (!connection.Success)
    throw new InvalidOperationException(connection.Message);

var channel = planet.Channels.FirstOrDefault(c => c.Id == channelId)
    ?? throw new InvalidOperationException("The channel is not available.");

async Task OnMessage(Message message)
{
    if (message.AuthorUserId == client.Me.Id || message.Content != "!hello")
        return;

    var reply = await channel.SendMessageAsync("Hello!");
    if (!reply.Success)
        Console.Error.WriteLine(reply.Message);
}

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    stopping.Cancel();
};

channel.MessageReceived += OnMessage;
try
{
    var opened = await channel.OpenWithResult("greeting-bot");
    if (!opened.Success)
        throw new InvalidOperationException(opened.Message);

    Console.WriteLine($"Listening in {channel.Name}. Press Ctrl+C to stop.");
    await Task.Delay(Timeout.Infinite, stopping.Token);
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
}
finally
{
    channel.MessageReceived -= OnMessage;
    await channel.Close("greeting-bot");
}
```

Opening a channel acquires a keyed real-time subscription. Closing it with the
same key releases that consumer's interest. HTTP operations and live subscriptions
are separate: sending a permitted HTTP request does not require listening for
all channel events.

## Receiving messages

`channel.MessageReceived` observes the selected channel. The client-wide
`client.MessageService.MessageReceived` event observes messages delivered through
the client's active subscriptions. It does not automatically subscribe to every
channel the account can access.

`BotService.JoinAllChannelsAsync()` is an initialization helper for bots that need
all joined planets and their channels. For a bot with a narrow purpose, opening
only its configured channels makes its behavior easier to control.

Events use `HybridEvent`, so asynchronous handlers can overlap and the caller does
not await their completion. Serialize work yourself if commands depend on order,
and observe failures from outbound requests. See
[Reactive models](../../Docs/ReactiveModelSystem.md) for event and cache behavior.

## Other SDK operations

`channel.GetLastMessagesAsync(count)` fetches recent history. A message can resolve
its user with `FetchAuthorUserAsync()` or its planet member with
`FetchAuthorMemberAsync()`. A direct message may not have planet-member context,
so handle a missing member.

`BotService` also exposes creation, updates, deletion, and token regeneration for
bots owned by the signed-in account. `InitializeBot(email, password)` supports
account-password login and opens joined channels; token login is the direct path
for tokens issued by the bot management screen.

For federation, use the SDK's origin-aware planet/channel models and explicit
community-domain acceptance. Do not use an unscoped numeric cache lookup to select
a channel on a community node. Keep replies tied to the channel that produced the
event, as in the example above.
