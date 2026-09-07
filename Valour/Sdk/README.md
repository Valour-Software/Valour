# Valour SDK

The SDK provides the .NET client used by Valour applications and bots.
`ValourClient` owns authentication, services, model caches, and node connections.
Services make HTTP requests and subscribe to SignalR events; synchronized models
keep a shared cached instance for components and other consumers.

The project targets `net11.0` and references `Valour.Shared`. Use the SDK pinned in
the repository's `global.json` when building from source. To develop against this
checkout, add a project reference to `Valour/Sdk/Valour.Sdk.csproj`.

`Client/` contains the entry point, `Services/` contains feature APIs, `Models/`
contains client models, `ModelLogic/` contains stores and query helpers, and
`Nodes/` manages HTTP and real-time connections. External community-node models
are scoped by origin so locally reused IDs do not collide.

See the repository's [bot guide](https://github.com/Valour-Software/Valour/blob/main/Valour/Docs/BOT_GUIDE.md)
for an example and [reactive model guide](https://github.com/Valour-Software/Valour/blob/main/Docs/ReactiveModelSystem.md)
for event lifetime, cache identity, and threading rules.
