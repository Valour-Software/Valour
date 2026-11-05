# Client compatibility

The server, the web app, and the desktop and mobile apps are released
separately, and the store apps update on each person's device over days or
weeks. While that happens, the current server serves people still running an
older app. The server therefore has to keep everything that the oldest
supported release uses: its HTTP routes, the hub methods it calls, the
realtime events it listens for, and the JSON properties it reads and sends.

Two kinds of tests check this. The wire contract tests record what the
current server exposes and compare it with a recorded baseline from the oldest
supported release. They are quick and need no services. The client journey
tests start the current server and use it through the SDK that the oldest
supported release shipped, so they also catch changes in behavior.

## What the contract records

[`WireContractBuilder`](../Valour/Tests/Contracts/WireContractBuilder.cs)
produces the contract of the code under test:

- **HTTP routes**: every route that `Program.MapApiRoutes` maps, with the types
  of its path parameters, its query, header, and form parameters, its JSON body,
  and which of these are required. A route appears as its method and path, with
  parameter names left out, such as `GET api/channels/{long}`.
- **Hub methods**: the public methods of `CoreHub` that clients invoke, with
  their parameter and return types.
- **Realtime events**: the names in [`HubEvents`](../Valour/Shared/HubEvents.cs),
  and a `{Name}-Update` and `{Name}-Delete` event for each server model that
  has a client model of the same name in the SDK.
- **JSON shapes**: the properties and property types of every type those use,
  the server models above, the data types in `Valour.Shared.Models`, and the
  SDK's encryption and embed wire types. Enums record their values and whether they
  are written as numbers or names. Polymorphic types record their discriminator
  values.

Shapes follow the web serializer defaults that minimal API responses and the
SignalR JSON protocol use: camel-case property names, and enums as numbers
unless the enum declares a string converter.

The builder creates the server's services and routes without applying
migrations, connecting to Redis, reading a settings file, or starting the host.
The tests therefore need no database or Redis and touch no outside service,
and they are safe to run on any machine:

```sh
dotnet test Valour/Tests/Valour.Tests.csproj --filter "FullyQualifiedName~Valour.Tests.Contracts"
```

The `.NET` workflow runs them after the build, and the `Client compatibility`
workflow runs them for every pull request to `main`. Both fail when they fail.

## What counts as a breaking change

[`WireContractComparer`](../Valour/Tests/Contracts/WireContractComparer.cs)
reports a change when an older client could notice it:

- a route, hub method, event, or recorded type is removed
- a path parameter, query parameter, request body, or hub method changes type
- a parameter or body becomes required, or a required one is added to an
  existing route
- a property is removed or changes type, including a value type becoming
  nullable or the reverse
- an enum value is removed or renumbered, or the enum switches between numbers
  and names
- a polymorphic type changes its discriminator or drops a derived type

Additions are allowed. Older clients ignore routes, events, enum values, and
JSON properties they do not know, and a server ignores query parameters and
properties it no longer reads. A new optional parameter is also allowed. An
enum written as names is the exception: an older client cannot read a name it
does not know, so a value added to such an enum must not reach older clients.

Every realtime event name must come from `HubEvents`. The
`HubEventsAreNamedThroughHubEvents` test fails when a server send or a client
listener in `Valour/Server`, `Valour/Sdk`, or `Valour/Client` writes the event
name as a string. Model events are named after the model type, so renaming a
server model or its SDK counterpart renames its events.

## When the test fails

The failure lists each change on its own line. For each one:

1. **Keep the old form.** Add the new route, property, or event alongside the old
   one, and remove the old one only after the baseline no longer uses it. This
   is the usual fix.
2. **Accept it, if no released client depends on it.** Some removals are safe,
   such as a property that no client reads. Add the change to
   [`accepted-breaks.txt`](../Valour/Tests/Contracts/accepted-breaks.txt)
   exactly as the test reports it, followed by ` # ` and the reason it is safe.
   The test fails if a line has no reason, or if a listed change no longer
   differs from the baseline.

The contract cannot show whether a client actually reads a property, so check
the SDK and client code of the baseline release before accepting a removal.

## Client journey tests

[`Valour/Tests.Compatibility`](../Valour/Tests.Compatibility/ClientJourneyTests.cs)
does what people do in the app on an ordinary day, against a running server and
only through the SDK:

- registering, signing in, and setting up encryption on a new device
- checking that the server does not ask this client to update
- creating a planet, joining it, and receiving its channel keys
- receiving new, edited, reacted, and deleted channel messages as they happen,
  decrypted, and replying in the same channel
- seeing a channel rename arrive while the channel is open
- direct messages, from history and as they arrive
- friend requests as they arrive

By default the project builds against the SDK in this checkout. Built with
`-p:ClientSdkVersion=0.9.20`, it uses the `Valour.Sdk` package published for
that release instead. The SDK is the part of an app that talks to the server,
so this runs the same requests, serialization, and event handling that people
with that release installed have. The tests can only use SDK features that
every tested release has.

[`Tools/Compatibility/run-client-tests.py`](../Tools/Compatibility/run-client-tests.py)
runs them. It publishes the current server, starts it against a PostgreSQL
database and Redis, then runs the tests once with the SDK in this checkout and
once with the released SDK of the [baseline](#moving-the-baseline):

```sh
python3 Tools/Compatibility/run-client-tests.py --db-host localhost:5432 --redis localhost:6379
```

The server applies migrations to that database, so its name must mark it as a
test database, and both hosts must be local or on a private network. The
database must already exist. The server reads its settings only from
environment variables the script sets: the script removes any settings file
from the published output, because a local settings file can hold credentials
for live services. Use `--client` to choose the clients, such as
`--client 0.9.19`, and `--server-url` to test a server that is already running.

A failure with the released SDK and not with the current SDK means the server
changed something that the released apps rely on. A failure with both usually
means the server or the test is broken. Without `VALOUR_COMPAT_SERVER`, which
the script sets, the tests are skipped, so building or testing the whole
solution does not need a server.

## Moving the baseline

[`baseline.json`](../Valour/Tests/Contracts/baseline.json) holds the contract of
the oldest release that installed clients still run, named in its `version`
field. That version also selects the released SDK that the client journey
tests use. Move it forward when a newer release has replaced that one in the
app stores and on the website, and older installs no longer need support:

```sh
Tools/Contracts/update-baseline.sh v0.9.21
```

The script builds the release in a temporary worktree, writes its contract to
`baseline.json`, and removes the worktree. Then clear `accepted-breaks.txt`,
because those changes are now part of the baseline. The tag must include the
wire contract tests.

Setting `VALOUR_UPDATE_WIRE_CONTRACT=1` while running the tests writes the
contract of the current code to `baseline.json` instead of comparing. The
script uses it, and it is only correct on a release's own commit.

## Ending support for older clients

The server reports `InstanceManifest.OldestSupportedClientProtocol` to clients
as the manifest's `MinimumClientProtocol`. A client whose
`InstanceManifest.CurrentClientProtocol` is lower shows a notice that cannot be
dismissed, asking the person to update. Raising `CurrentClientProtocol` alone
locks nobody out.

To drop support for a release, raise `OldestSupportedClientProtocol` above that
release's `CurrentClientProtocol`. The `BaselineReleaseIsStillSupported` test
then fails until the baseline moves to a release the server still supports.
Do this only after the stores have approved and rolled out a release that
speaks the new protocol, or people will be asked to install an update they
cannot get yet.

## Limits

The contract describes the server's interface, not its behavior. It does not
detect a property that is still sent but now always empty, a stricter
validation rule, or a different meaning for the same value. It also does not
track whether reference-type properties may be null.

API handlers return `Results.Json`, which carries no response type, so response
types are recorded only when they belong to one of the groups listed under
[What the contract records](#what-the-contract-records). An anonymous object or
a response class defined elsewhere in the server is not checked by the
contract.

The client journey tests cover those gaps only for what they do. A feature
they do not use, such as voice, uploads, or villages, is checked only by the
contract. They also run the SDK without the app's interface, so they do not
show that an older app displays new data correctly.
