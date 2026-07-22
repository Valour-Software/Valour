# Valour Bot Guide

This guide walks you through creating a bot on Valour, connecting it with the SDK, and getting it to do things in a planet (server).

## 1. Create a Bot Account

1. Log into [Valour](https://app.valour.gg) with your regular account.
2. Go to **Developer Settings** (user menu > Developer).
3. Click **Create Bot** and give it a name.
4. **Copy the token immediately** — it is only shown once. If you lose it, you can regenerate it from the bot's edit page (this invalidates the old one).

## 2. Add the Bot to a Planet

Bots join planets the same way users do — through invite links. Create an invite in your planet's settings and use it to join the bot via the API, or simply use the Valour client to add the bot to the planet from the member list.

You can join a planet programmatically with:

```csharp
await client.PlanetService.JoinPlanetAsync(planetId, "INVITE_CODE");
```

## 3. Project Setup

Create a new .NET console app and add the SDK:

```bash
dotnet new console -n MyValourBot
cd MyValourBot
dotnet add package Valour.Sdk
```

## 4. Minimal Bot Example

```csharp
using Valour.Sdk.Client;
using Valour.Sdk.Models;

// Create the client pointed at Valour's API
var client = new ValourClient("https://app.valour.gg/");
client.SetupHttpClient();

// Log in with the bot token you saved earlier
var loginResult = await client.InitializeUser("bot-YOUR-TOKEN-HERE");
if (!loginResult.Success)
{
    Console.WriteLine($"Login failed: {loginResult.Message}");
    return;
}

Console.WriteLine($"Logged in as {client.Me.Name} (ID: {client.Me.Id})");

// At this point, client.PlanetService.JoinedPlanets is populated.
foreach (var planet in client.PlanetService.JoinedPlanets)
{
    Console.WriteLine($"  Planet: {planet.Name} ({planet.Id})");
}

// Keep the process alive
await Task.Delay(Timeout.Infinite);
```

## 5. Connecting to a Planet and Sending a Message

After login, you need to **open a realtime connection** to a planet before you can interact with it. This sets up the SignalR connection that delivers live events.

```csharp
var planet = client.PlanetService.JoinedPlanets.First();

// Load the planet's data (channels, roles, members, etc.)
await planet.EnsureReadyAsync();
await planet.FetchInitialDataAsync();

// Open a realtime connection so we receive events
await planet.ConnectToRealtime();

// Grab the default chat channel
var channel = planet.PrimaryChatChannel;
if (channel is null)
{
    // Fall back to first chat channel
    channel = planet.Channels.FirstOrDefault(
        c => c.ChannelType == Valour.Shared.Models.ChannelTypeEnum.PlanetChat);
}

if (channel is not null)
{
    var result = await channel.SendMessageAsync("Hello from my bot!");
    Console.WriteLine(result.Success ? "Message sent!" : $"Failed: {result.Message}");
}
```

## 6. Listening for Messages

The SDK provides events at two levels:

### Global (all messages the bot can see)

```csharp
client.MessageService.MessageReceived += (message) =>
{
    Console.WriteLine($"[{message.TimeSent:HH:mm:ss}] {message.Content}");
};
```

### Per-Channel

```csharp
channel.MessageReceived += (message) =>
{
    Console.WriteLine($"#{channel.Name}: {message.Content}");
};
```

To receive channel-level events, you need to open the channel's realtime connection:

```csharp
await channel.OpenWithResult("my-bot");
```

## 7. Full Echo Bot Example

Putting it all together — a bot that echoes messages back (ignoring its own):

```csharp
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

var client = new ValourClient("https://app.valour.gg/");
client.SetupHttpClient();

var loginResult = await client.InitializeUser("bot-YOUR-TOKEN-HERE");
if (!loginResult.Success)
{
    Console.WriteLine($"Login failed: {loginResult.Message}");
    return;
}

Console.WriteLine($"Bot online: {client.Me.Name}");

// Connect to all planets and channels (BotService helper)
await client.BotService.JoinAllChannelsAsync();

// Listen for messages globally
client.MessageService.MessageReceived += async (message) =>
{
    // Ignore our own messages
    if (message.AuthorUserId == client.Me.Id)
        return;

    // Only respond to messages starting with "!echo "
    if (message.Content is null || !message.Content.StartsWith("!echo "))
        return;

    var reply = message.Content.Substring(6);

    // Get the channel and send the reply
    if (client.Cache.Channels.TryGet(message.ChannelId, out var channel))
    {
        await channel.SendMessageAsync(reply);
    }
};

Console.WriteLine("Listening for messages...");
await Task.Delay(Timeout.Infinite);
```

## 8. Using `BotService.InitializeBot` (Alternative Login)

If you prefer to log in with email/password instead of a token (e.g., during development), the `BotService` has a convenience method that also auto-connects to every planet and channel:

```csharp
var client = new ValourClient("https://app.valour.gg/");
var result = await client.BotService.InitializeBot("bot@example.com", "password");
```

This calls `LoginAsync`, sets up the primary node, and calls `JoinAllChannelsAsync()` automatically.

## Key Concepts

| Concept | Description |
|---------|-------------|
| **ValourClient** | The main SDK entry point. Holds all services and the logged-in user. |
| **Planet** | A server/community. Contains channels, roles, and members. |
| **Channel** | A text or voice channel within a planet (or a DM). |
| **Node** | A Valour server node. The SDK manages node connections automatically. |
| **PrimaryNode** | The main API node your client talks to. |
| **Realtime** | SignalR connections that deliver live events (messages, edits, etc.). |

## Common Patterns

### Get a specific channel by name

```csharp
var general = planet.Channels.FirstOrDefault(c => c.Name == "General");
```

### React to a message

```csharp
await message.AddReactionAsync("thumbsup");
```

### Fetch recent messages from a channel

```csharp
var messages = await channel.GetLastMessagesAsync(50);
```

### Check who sent a message

```csharp
var user = await message.FetchAuthorUserAsync();
var member = await message.FetchAuthorMemberAsync(); // planet context
Console.WriteLine($"Sent by: {member?.Nickname ?? user.Name}");
```

## Tips

- **Token security**: Never commit your bot token to source control. Use environment variables or a secrets manager.
- **Rate limits**: Be mindful of how often you send messages. Avoid tight loops.
- **Permissions**: Bots respect the same role/permission system as users. Make sure the bot's role has the permissions it needs (Send Messages, etc.).
- **Reconnection**: The SDK handles SignalR reconnection automatically through the `Node` system.
- **Max bots**: Each user account can create up to 10 bots.
