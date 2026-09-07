# Shared contracts and utilities

`Valour.Shared` contains code used by the SDK, client, database, and server. Shared
model interfaces define common fields and routes. Permission definitions describe
the available operations, and shared request/response types carry data across API
and real-time boundaries.

The project also contains reusable utilities such as `HybridEvent`, federation
contracts, and village scene and tileset types. It targets `net11.0`; build with
the SDK pinned in the repository's `global.json`.

Keep shared contracts independent of a particular UI or server service. A change
to a serialized field, enum, or route needs corresponding handling in its producers
and consumers. See [the reactive model guide](../../Docs/ReactiveModelSystem.md)
for event semantics and [the federation architecture](../../Docs/FederationArchitecture.md)
for origin and protocol requirements.
