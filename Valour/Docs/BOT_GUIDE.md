# Bot development

A Valour bot uses an account token and the same API and permission system as the
client. The SDK handles authentication, planet data, HTTP requests, real-time
subscriptions, and message encryption. This guide uses a source reference so the
example runs against the SDK in this checkout.

Every chat message is end-to-end encrypted, so a bot must send and read messages
through the .NET SDK, version 0.9.0 or later. A program that only needs to post
into one channel can use a [webhook](#webhooks) instead. See
[Programs that do not use the SDK](#programs-that-do-not-use-the-sdk) for what
other clients receive.

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
channel and removes its subscriptions when Ctrl+C stops the process.

```csharp
using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
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

// A message arrives before it can be read when the bot does not have the
// channel's key yet. Its text is empty until the key arrives, and then
// MessageDecrypted raises it again, so both events use this handler.
async Task OnMessage(Message message)
{
    if (message.ChannelId != channel.Id ||
        message.DecryptionState != MessageDecryptionState.Decrypted ||
        message.AuthorUserId == client.Me.Id ||
        message.Content != "!hello")
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
client.MessageService.MessageDecrypted += OnMessage;
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
    client.MessageService.MessageDecrypted -= OnMessage;
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

## Encrypted channels

The server refuses messages that are not encrypted. The SDK sets up the bot's
keys on first login and encrypts and decrypts messages automatically, so
`PostAsync`, `UpdateAsync`, and `MessageReceived` need no extra work. Private keys
and the recovery code are stored in a `.valour-e2ee` directory in the working
directory. On Linux and macOS the SDK makes that directory and its files readable
only by the account that runs the bot; on Windows, protect the directory
yourself. Set `client.E2eeService.KeyStore` before login to store them
elsewhere. Log in with `InitializeUser` or `InitializeBot`, which initialize
encryption; a bot that calls `AuthService.LoginAsync` directly must call
`E2eeService.InitializeAsync` itself.

The keys must survive restarts. A bot that starts without them cannot read or send
messages, and the SDK logs an error that names the key directory. In a container,
mount a volume at the key directory, for example `-v valour-bot-keys:/app/.valour-e2ee`
when the working directory is `/app`. To recover when the directory is lost, save
the recovery code once, after the bot's first login, from
`await client.E2eeService.GetStoredRecoveryCodeAsync()`, and provide it in the
`VALOUR_E2EE_RECOVERY_CODE` environment variable or in
`client.E2eeService.RecoveryCode` before login. The SDK then restores the keys
with it. Treat the code like the bot's token: anyone who has it can read the
bot's messages.

The SDK never resets a bot's keys on its own unless
`client.E2eeService.AllowAutomaticReset` is true, and even then only when the
keys cannot be restored: no stored key and no working recovery code, or a stored
key the account no longer lists. A reset makes earlier messages unreadable to the
bot and shows its contacts a key change notice. A failure to reach Valour never
causes a reset; `E2eeService.Status` is `Unavailable` and the SDK tries again.

Embeds are encrypted with the message, so the server cannot check them. The SDK
checks an embed before sending: it refuses one that loads media from sources that
are not allowed, and one the bot lacks the channel's Embed permission to post.
Recipients hide embeds that fail these checks too. A message sent through the SDK
carries one embed. To update an embed after sending, call
`message.SendEmbedUpdateAsync(update)` with `NewEmbedContent` or
`ChangedItemsContent` set. The SDK encrypts the update with the channel key.
Interaction events (button clicks and form submissions) reach the bot in plain
form, including interactions on the bot's messages from before encryption,
whether they are still plain text or the server has sealed them as `Legacy`
messages.

Custom planet emojis in a message need the planet's Use Custom Emojis permission,
and the SDK refuses them in direct messages. The SDK lists the emoji IDs with the
encrypted message so the server can check them.

A bot can read an encrypted channel only after another member's device shares
its key, which happens when a member with the key is online. Until then a
message's `DecryptionState` is `WaitingForKey` and its `Content` is empty.
`MessageReceived` has already fired for it by then, so handle
`client.MessageService.MessageDecrypted`, which fires when a waiting message
becomes readable, as the example above does. `Message.DecryptionChanged` fires
on the message itself at the same time. See
[End-to-end encryption](../../Docs/EndToEndEncryption.md) for details.

A planet on a community node that has not been updated for encryption cannot
take messages from current clients. Sends there fail with "This community node
must be updated before you can send messages here." `Node.ClientProtocol` holds
the protocol the node reported.

## Programs that do not use the SDK

The server refuses a message that is not encrypted. A raw HTTP client, or an SDK
version before 0.9.0, that posts to `api/messages` or edits a message receives a
400 response whose text begins with `E2EE_REQUIRED:`. Check for that prefix to
tell this apart from other errors. The fix is to move the bot to the .NET SDK
0.9.0 or later, or to post through a webhook.

## Webhooks

A webhook posts into one channel through a URL that contains its token, with no
account or SDK needed. Create one in the planet's Webhooks settings. Send a JSON
`WebhookExecuteRequest` to `POST api/webhooks/{id}/{token}` with `Content`, up to
five `Embeds` as serialized embed JSON, and optional media `Attachments`. The
SDK's `WebhookClient` builds these requests. Webhook text is sent to the server
in plain form; the server seals it to the channel's key right away and keeps only
the sealed copy, so members read it like other encrypted messages.

Because the server cannot read a webhook message after sealing it, an edit through
`PUT api/webhooks/{id}/{token}/messages/{messageId}` replaces the message's text
and embeds. `WebhookMessageEditRequest` requires both `Content` and `Embeds`; use
an empty string or an empty list to remove them. A request missing either field
is refused. Media attachments from the original message are kept.

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
