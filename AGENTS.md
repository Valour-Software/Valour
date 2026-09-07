# Guidance for AI agents

## Read the relevant documentation

Start with [Docs/README.md](Docs/README.md). Before changing model synchronization,
read [Docs/ReactiveModelSystem.md](Docs/ReactiveModelSystem.md). This applies to
`Valour/Sdk/ModelLogic/`, `Valour/Sdk/Nodes/Node.cs`,
`Valour/Shared/Utilities/HybridEvent.cs`, `Valour/Server/Hubs/CoreHub.cs`, and
`Valour/Server/Services/CoreHubService.cs`.

## Project structure

`Valour/Client/` is the shared Razor UI library, and `Valour/Client.Blazor/` is its
browser WebAssembly host. `Valour/Client.Maui/` contains the native host. The SDK
owns client models, services, caches, and node connections. `Valour/Shared/` owns
shared contracts and utilities. Database models and migrations live in
`Valour/Database/`; APIs and application services live in `Valour/Server/`.
`Config/` contains server settings, and `Valour/Web/` contains the public website.

## Threading and model identity

Model stores synchronize collection operations internally with `SyncLock` and
invoke events after releasing it. Do not add external locking around ordinary
store access or invoke handlers while holding collection locks. Enumeration
returns a list snapshot, not immutable copies of its models.

`HybridEvent` supports synchronous and asynchronous subscribers. Invocation does
not wait for asynchronous handlers to finish. Copy needed values from pooled
change data before scheduling later work. Remove subscriptions when their owner
is disposed, and dispose only events that owner created.

Community-node models carry origin scope. Preserve that scope in HTTP requests,
cache lookups, and synchronization. A local numeric ID alone does not identify an
object across independent community nodes.

## Build and verification

Use the exact SDK pinned in `global.json`. Build with `dotnet build` and run the
local server with `dotnet run --project Valour/Server/Valour.Server.csproj`.
The server applies database migrations on startup.

C# integration tests require isolated PostgreSQL and Redis services. Follow
[the isolated test instructions](Valour/Tests/Browser/README.md#isolated-c-regression)
and use the local test runner. JavaScript tests run through Node's test runner;
[their README](Valour/Tests/Js/README.md) explains compilation requirements.

## Documentation style

Keep documentation aligned with the code in the checkout. Describe current
behavior and constraints without implementation history, replacement narratives,
or proposed features. Use readable English with enough explanation to understand
the concepts. Do not use em dashes, promotional phrasing, or unexplained jargon.
Update relevant guides when changing architecture or externally visible behavior.
