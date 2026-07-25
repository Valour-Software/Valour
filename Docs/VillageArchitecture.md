# Valour Village Architecture

Villages are a planet-scoped 2D social world embedded in the Valour client: a
place members walk around, run into each other, and talk, with buildings that
surface the planet's existing channels rather than duplicating them.

This document describes **what is built**, the reasoning behind decisions that are
not obvious from reading the code, and what is deliberately still open. Anything
under "Not built yet" is not in the codebase.

## Shape of the system

```text
SERVER
  village_maps / village_map_chunks / village_plots
  village_buildings / village_objects        persistent world
    -> VillageWorldService                   load + seed
    -> VillageMarketService                  ownership and sales
    -> VillagePresenceService                ephemeral occupancy
    -> CoreHub                               per-map realtime groups

CLIENT SDK
  VillageService            scene fetch, presence subscription
  ModelStores on Planet     maps, plots, buildings, objects

CLIENT
  VillageWindowComponent    Blazor shell: HUD, inspectors, voice binding
    -> .razor.js            canvas runtime: render loop, input, collision
    -> VillageSpatialAudio  positional voice graph
    -> VillageTileRendering shared tile/texture helpers
```

Blazor owns window lifecycle, HUD, permissions-aware controls and channel
integration. The canvas runtime owns the render loop, camera, input, collision and
interpolation. The boundary matters: per-frame work must never round-trip through
Blazor's render tree.

## Persistent world

Five planet-scoped entities, each following the standard `ISharedPlanetModel` /
`ClientPlanetModel` pattern so they sync through existing realtime plumbing.

- **`VillageMap`** — an outdoor world or a building interior. Interiors are ordinary
  maps with a `ParentBuildingId`, not a special case, so neither the renderer nor
  the persistence layer needs to know the difference.
- **`VillageMapChunk`** — a fixed 32×32 square of tile content. `LayerData` and
  `CollisionData` are opaque blobs so the renderer can gain layers without a schema
  migration.
- **`VillagePlot`** — the unit of ownership and sale; gates who may build.
- **`VillageBuilding`** — the bridge to the rest of Valour: optional `ChannelId`,
  optional `InteriorMapId`, a door tile, and a `VoiceMode`.
- **`VillageObject`** — props, stored separately from chunk data because they are
  individually owned, moved, and depth-sorted against characters.

### Why chunks from the start

A map's tiles are the only thing that grows without bound. Chunking lets a large
world load and save in pieces, and costs nothing for a small interior, which simply
occupies one chunk. Retrofitting it would have meant rewriting every read and write
path.

### Why only one foreign key

Every entity has a cascading FK to `Planet` and nothing else. `MapId`, `PlotId`,
`InteriorMapId`, `OwnerMemberId` and `ChannelId` are plain columns. Wiring those as
real FKs creates circular cascade paths Postgres rejects — a building points at an
interior map, which points back at its parent building.

## Presence

Presence is **ephemeral, per-node, and never persisted**.

Planets are node-pinned, so every member of a village is served by the same node
and an in-memory dictionary suffices. Losing it on restart is correct rather than
lossy: clients re-announce on reconnect. Writing a position that changes several
times a second to a table would be write amplification for data that is worthless
once stale.

Points that are easy to get wrong:

- **Positions are tile-quantized.** A move is two small ints; clients interpolate
  between tiles. Streaming floats multiplies traffic for no visible gain.
- **Identity travels on join and in snapshots only, never on movement.** A name and
  avatar do not change while someone walks, so the client must never overwrite a
  known name with the blank one a movement-derived record carries.
- **Groups are per map** (`v-{planetId}-{mapId}`), not per planet. Someone walking
  around the square should not wake every client on the planet.
- **Disconnects clear presence**, or a character stands in the world forever.
- **Facing is carried but not yet rendered**, so directional art can arrive without
  a protocol change.

## Interiors and movement

- A building's door tile is excluded from its own collision footprint, so doors are
  reachable without the runtime special-casing them.
- Every interior gets an exit portal on its spawn tile leading back to the door it
  was entered through. An interior without one traps the member.
- Collision is **derived from the authored objects** — an object that blocks, a
  building footprint, a map-level blocker — rather than a parallel list kept in sync
  by hand. The proof of concept maintained both and they drifted.
- Walking through a door moves the client between presence groups as well as maps.

`VillageWorldApiLiveTests` asserts these: spawn and every door are walkable, every
interior has an exit, every door targets a map that exists.

## Voice

Buildings surface voice in one of three modes (`VillageVoiceMode`):

- `None` — no voice.
- `LinkedChannel` — bound to an existing planet voice channel.
- `AutoRoom` — **not implemented.** Valour has no ephemeral channel concept: nothing
  creates a channel on demand and nothing reaps it. Until that exists, buildings in
  this mode are treated as not hosting voice rather than silently doing nothing when
  someone walks in.

### Auto-join is opt-in

Following someone between rooms is the point of the feature, but
`GlobalCallSessionService` has no start-muted option, so auto-joining would open a
microphone because a member wandered through a door. Auto-join is a per-session
toggle; with it off, buildings offer an explicit join button.

Two inherited constraints: the call layer handles one channel at a time, and
`MinimumRealtimeKitParticipants = 2` means a lone member never connects to the SFU.

### Positional voice

Each remote participant's microphone is routed through its own
`MediaStreamSource → PannerNode → GainNode`. The listener stays at the origin and
sources are positioned relative to it, avoiding the need to keep listener
orientation in sync with a top-down camera that never rotates.

Two non-obvious requirements:

- The `<audio>` element the call layer created is kept **alive but muted**. Browsers
  stop delivering a remote WebRTC track not attached to a media element, so removing
  it silences everyone.
- Positions are applied **per frame from eased render positions**, not per network
  update, so panning follows what the player sees, and are ramped rather than set so
  stepping a tile does not click.

## Ownership and sales

Listing requires `ManageVillage`; buying requires only membership, since the economy
decides affordability. Unowned property is sold by the planet so proceeds reach a
shared account rather than vanishing.

**Payment and handover are two commits.** `EcoService.CreateTransactionAsync` opens
and commits its own database transaction and cannot enlist in an ambient one. The
buyer is charged *first* and the deed moves *second*, so the failure that can
actually happen is recoverable: a retry completes the sale instead of charging
again, because the transaction fingerprint is derived from the sale rather than
random and the existing unique index enforces it. Handing over the deed first and
then failing to take payment would not be recoverable without clawing property back.

## Permissions

`ManageVillage` (`0x400000`) gates map editing and market listings. The tileset
definition editor is **staff-only**, not per-planet: definitions are shared platform
content, and anyone able to rename a tile key can break maps on planets they have
nothing to do with.

## Mobile

Movement is a floating stick that appears wherever the finger lands rather than in a
fixed corner, so it works in either hand and never covers what the player was
looking at. A drag past a deadzone steers; a touch that never passes it falls
through to the building hit-test, so tapping to inspect needs no second gesture.

Pointer events with capture rather than raw touch events, so a finger sliding off
the canvas keeps steering. `pointermove` is the only non-passive listener because it
is the only one that must `preventDefault`, and the canvas takes
`touch-action: none` so steering does not pan the page.

Mobile detection is a **user-agent sniff** into a static flag never re-evaluated on
resize, so a desktop window narrowed to phone width is still treated as desktop.

## Gotchas worth keeping

- The canvas backing store is measured before the dock lays the pane out, and the
  `resize` event is on `window`. A `ResizeObserver` **and** a per-frame drift check
  are both needed: the observer alone stops delivering, leaving a few-pixel backing
  store stretched across the window.
- The runtime owns a rAF loop and window-level key listeners, so its JS `dispose()`
  must be invoked explicitly. Releasing only the .NET reference leaves both running
  and `preventDefault`-ing WASD, which breaks typing app-wide.
- Movement keys are captured at the window level and must be gated on not being in a
  text input and on the canvas being visible. `keyup` must *not* be gated, or a key
  released after focus moves away leaves the player walking forever.

## Testing

- `Valour/Tests/Services/VillagePresenceServiceTests.cs` — presence semantics against
  the real service resolved from the running server. `CoreHubService` has too many
  collaborators to fake usefully, and its broadcasts into empty hub groups are
  harmless in tests.
- `Valour/Tests/Services/VillageMarketServiceTests.cs` — sale and fingerprint rules.
- `Valour/Tests/Apis/VillageWorldApiLiveTests.cs` — persistence and playability
  invariants. Shares one planet per class; the test user has an owned-planet cap and
  one planet per test method exhausts it.
- `Valour/Tests/Js/*.test.mjs` — the runtime's texture cache and the positional audio
  graph, via `node --test`. See that folder's README.

## Not built yet

- **Rendering authored tileset art.** The runtime still draws buildings, props and
  terrain as coloured primitives over a tiled base texture. Tile layers, sprite depth
  sorting against characters, culling and any lighting pass are open.
- **A beautiful default map.** The seeded world is functional geometry, not art
  direction.
- **Tileset breadth.** The default set covers 54 tiles. The Modern Exteriors sheet is
  2816×8224 (~90,000 tiles at 16px), packed edge-to-edge with no blank separator rows
  or columns, so connected-component and guillotine segmentation both fail — sprite
  bounds cannot be derived automatically. The intended approach is a grid picker over
  the raw sheet plus curated named definitions for the tiles a map actually uses.
- **Map editor round-trip.** The editor can export a map but not load one, and its
  tile/sprite format does not yet bridge to the runtime scene format.
- **Ephemeral voice rooms** (`AutoRoom`), needing a channel lifecycle Valour lacks.
- **Voice and video presented inside the world** rather than in the dedicated call
  window.
- **Server-side movement validation.** Collision is enforced client-side only;
  `CollisionData` is stored so the server can validate moves, but it does not yet.
- **Character appearance.** Characters are member avatars drawn as tokens. Layered
  sprite composition, and the directionality `VillageFacing` already carries, are
  open.
