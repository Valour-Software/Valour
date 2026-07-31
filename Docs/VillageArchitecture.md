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
- **Same-map movement is server constrained to one cardinal tile.** Portals join a
  different map, so there is no legitimate same-map teleport; forged jumps are
  rejected before they can update or throttle the real presence.
- **Identity travels on join and in snapshots only, never on movement.** A name and
  avatar do not change while someone walks, so the client must never overwrite a
  known name with the blank one a movement-derived record carries.
- **Groups are per map** (`v-{planetId}-{mapId}`), not per planet. Someone walking
  around the square should not wake every client on the planet.
- **Disconnects clear presence**, or a character stands in the world forever.
- **Reconnects rejoin at the last local tile.** SignalR groups and server presence
  disappear with the old connection, so the SDK re-announces the current map after
  node authentication is restored. Presence records carry their owning connection
  id internally: a late disconnect callback from the old socket cannot remove or
  move the newly restored presence.
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
- Map joins warm a process-wide immutable collision snapshot derived from bounds,
  object tileset masks, building footprints, authored doors, and chunk
  `CollisionData`; movement packets perform only an in-memory lookup. Chunk
  collision is JSON containing either a row-major 32×32 boolean array or
  `{ "blocked": [tileIndex, ...] }`. Malformed chunks are blocked fail-closed.
  Map-authoring writes must call `VillageCollisionService.InvalidateMap` after
  committing a collision-affecting change.
- Walking through a door moves the client between presence groups as well as maps.
- Interiors use a distinct stone floor and arrive furnished as meeting rooms. The
  parent building remains the room context while the member is inside, so chat,
  voice, occupancy, and the exit all agree about where the member is.
- Map transitions send the runtime's actual destination tile back to presence.
  Building context is derived from the persisted map rather than trusted from the
  client. Rejoining at the default spawn instead makes the next legitimate step
  look like a forged teleport. Interiors render one integer zoom step closer than
  outdoors so their smaller maps retain a camera-follow range.
- The toolbar buttons step zoom through the familiar 25% levels, while the mouse
  wheel and trackpad steer a continuous multiplicative target; both ease toward
  their destination in the frame loop rather than snapping. Relative zoom is
  bounded from 50% to 200%, with a one-tap 100% reset; the map-specific outdoor
  or interior render scale remains the baseline. During a glide the static layer
  is blitted scaled from its previous composite and recomposed once the zoom
  settles, so animating never recomposes the whole map per frame. The HUD's whole-
  percent readout is updated at most every 100ms mid-glide, because each report
  re-renders the Blazor side.

`VillageWorldApiLiveTests` asserts these: spawn and every door are walkable, every
interior has an exit, every door targets a map that exists.

## Voice

Buildings surface voice in one of three modes (`VillageVoiceMode`):

- `None` — no voice.
- `LinkedChannel` — bound to an existing planet voice channel.
- `AutoRoom` — an unlinked building leases a hidden, video-capable planet call
  channel and its integrated chat on first entry. Occupants share the same lease;
  the channel is soft-deleted twenty seconds after the last occupant leaves, with
  reacquisition cancelling that cleanup. Disconnect cleanup releases every lease,
  and the first room request after a server restart reaps any orphaned channels.
  An open village reacquires its lease after SignalR presence is restored; if a
  restart replaced the channel, an active village call follows the replacement.
  Rebinding a building retires its old temporary room immediately.

These channels deliberately reuse the normal chat and call transports, but the
directory filters their `◇ ` internal names: to a member they exist only as the
meeting dock, nearby composer and speech bubbles inside the village. The acquire
route also checks live building presence, and movement/context updates are awaited
before acquisition, so knowing a building id does not grant remote access to its
area room.

### Auto-join is opt-in

Following someone between rooms is the point of the feature, but
`GlobalCallSessionService` has no start-muted option, so auto-joining would open a
microphone because a member wandered through a door. Auto-join is a per-session
toggle; with it off, buildings offer an explicit join button.

Two inherited constraints: the call layer handles one channel at a time, and
`MinimumRealtimeKitParticipants = 2` means a lone member never connects to the SFU.

The village embeds `CallPanelComponent` in a responsive world-side meeting dock.
Audio rooms show their roster and controls without opening another window; video
rooms show the existing grid, focus, screen-share and moderation UX in the same
surface. On phones this becomes a bottom sheet, with nearby chat immediately above
it.

## Nearby text

The default planet chat is the outdoor commons conversation, so members can type
without first opening a channel window and see the result immediately above their
avatar. Inside a building, the composer follows that building's chat channel; voice
and video venues use their associated chat channel when one exists. Unlinked
buildings acquire the associated chat from their `AutoRoom`, so private properties
have the same nearby-text UX without creating permanent navigation. Incoming
messages only become bubbles when the speaker is present on the same map and the
message belongs to that spatial context. Bubbles are short-lived, capped and
three-line-wrapped; the linked channel remains the durable scrollback.

### Positional voice

Each remote participant's microphone is routed through its own
`MediaStreamSource → PannerNode → GainNode`. The listener stays at the origin and
sources are positioned relative to it, avoiding the need to keep listener
orientation in sync with a top-down camera that never rotates.

Two non-obvious requirements:

- The `<audio>` element the call layer created is kept **alive but muted**. Browsers
  stop delivering a remote WebRTC track not attached to a media element, so removing
  it silences everyone.
- The canvas runtime resolves each call participant's real `<audio>` element from
  its peer id and adopts its live `MediaStream`. With spatial mode off the original
  element is audible; with it on that element is muted and the panner graph is the
  only audible route, preventing doubled audio.
- Positions are applied **per frame from eased render positions**, not per network
  update, so panning follows what the player sees, and are ramped rather than set so
  stepping a tile does not click.

## Ownership and sales

Owners may list, update, or delist their own property directly from the world
inspector; members with `ManageVillage` may do the same for community property.
Buying requires membership and economy-send authority, since the economy decides
affordability. Unowned property is sold by the planet so proceeds reach a shared
account rather than vanishing. A planet without a configured currency seeds free
claimable property instead of advertising a purchase that can never settle.

Every purchase passes through the platform's confirmation modal with the price
spelled out - a mis-tap in the world must not spend currency. Property changes
broadcast through the standard planet model sync, and an open village window
subscribes to the building and plot stores: any member's purchase, listing, or
edit refreshes every open scene within half a second (debounced, selection
preserved), so the world never shows a stale deed.

The HUD is styled with the platform's design tokens (`--modal-dark`, the tint
and radius scales, `--color-success`/`--color-warning`/`--color-danger` for
ownership, sale, and destructive accents) rather than bespoke colors, so the
village reads as part of the app and follows any future theme changes.

Property is also editable in place from the same inspector: an owner may rename
and re-describe what they own, and `ManageVillage` may additionally rebind (or
clear) a building's linked channel — clearing it turns the venue back into
private property served by leased area rooms. Rebinding is manager-only because
it surfaces a planet channel to everyone who walks in. The scene carries a
`CanManageVillage` flag so the client knows what UI to offer, but every
management request is re-authorized server-side.

**Payment and handover are two commits.** `EcoService.CreateTransactionAsync` opens
and commits its own database transaction and cannot enlist in an ambient one. The
buyer is charged *first* and the deed moves *second*, so the failure that can
actually happen is recoverable: a retry completes the sale instead of charging
again, because the transaction fingerprint is derived from the sale rather than
random and the existing unique index enforces it. Handing over the deed first and
then failing to take payment would not be recoverable without clawing property back.
Each time a property is newly listed it receives a persisted sale id. That id is
part of the fingerprint, so a later A→B→A→B ownership cycle cannot reuse the first
payment as though the final purchase were a retry. Listing changes and purchases
are serialized through the same per-asset lock on the planet-pinned node,
preventing terms or ownership from changing while a buyer settles.

## Permissions

`ManageVillage` (`0x800000`) gates map editing and listing property the member does
not own. Ownership itself authorizes listing that asset. The tileset definition
editor is **staff-only**, not per-planet: definitions are shared platform content,
and anyone able to rename a tile key can break maps on planets they have nothing to
do with.

## Rendering and starter world

The runtime loads the map's tileset definition file, resolves logical keys to source
rectangles on the shared sheet, culls off-screen art, and draws props, buildings and
members in one bottom-edge-sorted pass. A member can therefore walk behind a canopy
or in front of a bench. Building hit targets and selection bounds use the visible
sprite. Props use one shared bottom-anchor calculation for drawing, culling and
collision: the scene footprint describes ground contact, while the tileset's
row-major collision mask selects the exact blocking cells. Tree canopies therefore
overhang walkable tiles, trunks and planters remain solid, and transparent padding
cannot shift the visible or physical bounds.

Objects with a negative `ZIndex` are ground tiles and render before plots and
characters. This supplies a persistent lightweight terrain layer today without
waiting for map-chunk authoring to round-trip through the editor.

The camera does not centre the player in the raw canvas: the floating HUD
panels overlap it, so the runtime measures how deep the top panels and the
bottom composer reach and centres the player in the strip they leave clear.
Near a map edge, where the usual clamp would pin the player underneath a
panel, the camera overscrolls past the edge by exactly as much as it takes —
the strip beyond the map is letterboxed background. The blit and culling paths
must therefore tolerate a negative camera.

Everything static — the base tile fill and the ground layer — is composited
once into an offscreen canvas per (map, scale) and blitted per frame. Before
this, a zoomed-out commons issued ~2,600 `drawImage` calls per frame and ran at
25fps; with the layer cache a full frame draw costs well under a millisecond.
The layer is invalidated when the map, scale, scene, or a late-loading texture
changes. Two sharp edges: the rebuild callback must only be subscribed to
textures still in flight (the texture cache also fires callbacks for loaded
textures, which would recompose in an endless microtask loop), and maps whose
pixel area would exceed Safari's canvas ceiling fall back to per-frame culled
drawing.

New planets receive a 52×40 landscaped commons rather than three blocks on grass:

- a cross-shaped promenade and cobbled central square;
- framed woodland, flower beds, planters, benches, a fountain and market stall;
- Town Hall chat, a voice lounge and a video studio bound to existing channels;
- furnished, reversible interiors for every venue;
- a purchasable Maker House and an independent claimable parcel;
- free listings when the planet has no currency, economy-backed prices when it does.

Sprite loading is intentionally fail-soft. A missing sheet or unknown key falls back
to the old primitive, so an incomplete community tileset remains navigable.

## Terrain rulesets

Ground materials are painted as **terrain**, not as concrete tiles. The tileset
declares terrains (key, name, priority) and annotates tiles with a terrain role:
`Base` tiles fill the material (several may carry weights — variants are picked
by a per-cell hash so a recomposite never reshuffles them), while `Edge`,
`Corner` and `InnerCorner` tiles describe how the material meets a neighbor. An
edge's direction names the side the *other* material is on, and `Against` limits
a transition to one specific neighbor — left empty it matches any. The resolver
(`VillageTileRendering.ts`, shared by the editors and available to the runtime)
reads each cell's 8-neighbor mask and picks art down a fail-soft ladder: a
missing piece renders the base tile and a hard seam, never a hole, so a
partially-authored family stays usable. The curated set has no inner corners
yet, and tall grass deliberately skips them — its `*1`/`*2` pieces are diagonal
half-fades that would carve a visible plain bite into a dense field, whereas a
hard diagonal is invisible in outline-free speckle art.

Exactly one side of a boundary draws transition art, or both materials fringe
into each other. The rule is art-driven: the side with authored art for the pair
wins outright; art authored for the specific pair beats a wildcard; then higher
priority wins, with the key as a deterministic tiebreak. This is why dark grass
(priority 15, authored specifically against dirt path) out-draws the higher
priority path (20, wildcard art) — the specific art is the better art.

The map editor's Terrain tool writes material keys into a terrain grid and
re-resolves the whole grid once per tool application — painting a second stroke
across an L-bend fixes the seams the fixed 3x3 brush stamps got wrong, which is
the problem this system replaces. Terrain cells own their tile-layer cell;
hand-painting a tile evicts the cell from the terrain grid so the resolver
cannot repaint it, and its neighbors re-resolve because an inert neighbor
changes their masks. Out-of-bounds and unpainted cells are inert: map edges and
hand-tiled areas do not sprout fringes. The exported map JSON carries both the
resolved tile layer (renderable without a resolver) and the terrain grid (so a
future load keeps painting with rulesets).

Terrain metadata lives in its own `Terrain*` fields rather than overloading the
existing group fields, whose semantics are already taken: `GroupKey` is a naming
category ("Grass" spans light grass, dark grass and dirt path) and `Direction`
is art facing (a bench faces South). The staff tileset editor authors the
terrain list and per-tile roles, and outlines terrain-annotated tiles in green
on the sheet.

## Mobile

Movement is a floating stick that appears wherever the finger lands rather than in a
fixed corner, so it works in either hand and never covers what the player was
looking at. A drag past a deadzone steers; a touch that never passes it falls
through to the building hit-test, so tapping to inspect needs no second gesture.
Because an invisible control is an undiscoverable one, touch devices also show a
ghosted joystick resting bottom-left (above whatever bottom HUD is present).
Touching it anchors the stick there like a classic fixed pad; touching anywhere
else keeps the float-under-finger behaviour. The ghost shows on the mobile
user-agent sniff or any `maxTouchPoints`, and any real touch turns it on, which
covers touch hardware both signals miss.

Two fingers pinch-zoom: the second finger converts the gesture from steering to
a pinch (and marks the session so its release is never mistaken for an inspect
tap), and the finger-distance ratio steers the same eased zoom target the wheel
uses, so pinching glides and the mid-glide scaled static-layer blit applies.
When a finger lifts the pinch ends; the remaining finger's origin is stale, so
it deliberately does not resume steering - a fresh touch does.

Pointer events with capture rather than raw touch events, so a finger sliding off
the canvas keeps steering. `pointermove` is the only non-passive listener because it
is the only one that must `preventDefault`, and the canvas takes
`touch-action: none` so steering does not pan the page.

Mobile detection is a **user-agent sniff** into a static flag never re-evaluated on
resize, so a desktop window narrowed to phone width is still treated as desktop.
Layout itself is width-responsive: at phone sizes the meeting dock becomes a bottom
sheet, the chat composer sits directly above it, verbose brand copy disappears,
controls become icon-sized touch targets, and the inspector yields while a meeting
is open so the world never collapses under stacked panels.

## Gotchas worth keeping

- The canvas backing store is measured before the dock lays the pane out, and the
  `resize` event is on `window`. A `ResizeObserver` **and** a per-frame drift check
  are both needed: the observer alone stops delivering, leaving a few-pixel backing
  store stretched across the window. The tileset editor and the map editor need
  the same treatment, where the symptom is nastier: a stretched canvas draws in
  one coordinate space while mouse hit-testing computes in another, so clicks
  select tiles away from the cursor (worst when zoomed in). Neither has a frame
  loop, so alongside their observers they re-check for drift at the start of
  every mouse-down and wheel event, before any hit-test runs. In the map editor
  the drift reached 4x (backing 203px versus 890px laid out) on first open.
- The map editor's `draw()` used to pass a redraw callback to `loadTexture` on
  every call. The texture cache fires callbacks for already-loaded textures via
  microtask, so once the sheet loaded, draw -> callback -> draw starved the main
  thread and hard-hung the tab - the same endless-microtask loop the runtime's
  static-layer cache guards against. Subscribe to a texture once at init; per-draw
  lookups must not register callbacks.
- Paint tools must interpolate between mouse events. A fast drag jumps several
  cells per `mousemove` and an uninterpolated stroke leaves diagonal pinholes -
  invisible with plain tile painting, obvious once terrain fringes every hole.
- The runtime owns a rAF loop and window-level key listeners, so its JS `dispose()`
  must be invoked explicitly. Releasing only the .NET reference leaves both running
  and `preventDefault`-ing WASD, which breaks typing app-wide.
- Movement keys are captured at the window level and must be gated on not being in a
  text input and on the canvas being visible. `keyup` must *not* be gated, or a key
  released after focus moves away leaves the player walking forever.
- **Snowflake ids exceed JavaScript's 2^53 float precision.** An id serialized as a
  JSON number is silently rounded by `JSON.parse`, and the rounded value coming back
  from a click hit-test matches no building or plot, so the inspector simply never
  opens. Every id in the scene payload is therefore serialized as a string
  (`JsonNumberHandling.WriteAsString`), the `[JSInvokable]` callbacks take string ids
  and parse them, and ids passed *into* the runtime (`setMap`, `pushBubble`,
  presences, voice peers) are stringified first. The runtime only ever compares ids
  for equality, so it never notices.
- The window component inherits `ControlledRenderComponentBase`, which suppresses
  Blazor's automatic post-event renders. Every handler that changes visible state
  must call `ReRender()` itself, or the click lands, the state changes, and nothing
  on screen moves.
- The SDK's scene fetch bypasses the node's short GET cache
  (`cacheDurationMs: null`): the client refetches the scene immediately after a
  purchase or edit, and the cached pre-edit world would swallow the change.

## Testing

- `Valour/Tests/Services/VillagePresenceServiceTests.cs` — presence semantics against
  the real service resolved from the running server. `CoreHubService` has too many
  collaborators to fake usefully, and its broadcasts into empty hub groups are
  harmless in tests.
- `Valour/Tests/Services/VillageMarketServiceTests.cs` — sale and fingerprint rules.
- `Valour/Tests/Apis/VillageWorldApiLiveTests.cs` — persistence and playability
  invariants, plus the property-management rules: rename/redescribe, channel rebind
  and unbind, rejection of foreign or non-surfaceable channels and unusable names,
  and denial for a member who neither owns the asset nor holds `ManageVillage`.
  Shares one planet per class; the test user has an owned-planet cap and one planet
  per test method exhausts it. Tests that mutate the world restore it, because every
  test in the class reads the same planet.
- `Valour/Tests/Js/*.test.mjs` — the runtime's texture cache, the positional audio
  graph, and the terrain autotile resolver (side rule, bitmask ladder, fail-soft
  fallbacks, deterministic variants), via `node --test`. See that folder's README.

## Not built yet

- **Chunk tile-layer playback.** Ground objects render today, but the opaque
  `VillageMapChunk.LayerData` format is not yet decoded by the runtime. A
  terrain-key-per-cell grid is the natural format: the resolver already lives in
  the shared rendering module, so the runtime can adopt it at composite time
  without new resolution code.
- **Tileset breadth.** The curated default set covers 63 tiles and sprites.
  The definition editor's selection is adjustable in place: the selection
  rectangle carries corner and edge grab handles (with matching resize
  cursors), dragging an edge across its anchor flips the rectangle like the
  rubber-band select, and a drag that wanders past the sheet edge pins to the
  edge. Selection reports during any drag are marked *live* and skip
  definition matching - only the report on release may load a saved
  definition, so passing through a rectangle that happens to equal one cannot
  hijack the draft mid-gesture. Saving keeps the saved definition loaded for
  continued editing - resetting the panel on save wiped the collision mask and
  identity fields the moment they were saved, which read as data loss. The
  sheet view follows design-tool gesture conventions: plain wheel or
  two-finger scroll pans in both axes, and a trackpad pinch (delivered by
  browsers as a ctrl-wheel) or explicit ctrl/cmd wheel zooms anchored at the
  cursor, with a per-event factor clamp so a single mouse-wheel tick cannot
  leap across zoom levels. The Modern Exteriors sheet is
  2816×8224 (~90,000 tiles at 16px), packed edge-to-edge with no blank separator rows
  or columns, so connected-component and guillotine segmentation both fail — sprite
  bounds cannot be derived automatically. Continue with the existing grid picker and
  curated named definitions for art a map actually uses.
- **Map editor round-trip.** The editor can export a map but not load one, and its
  tile/sprite format does not yet bridge to the runtime scene format. Exports
  already carry the terrain grid alongside the resolved tiles so loading can
  restore ruleset painting, not just pixels.
- **Character appearance.** Characters are member avatars drawn as tokens. Layered
  sprite composition, and the directionality `VillageFacing` already carries, are
  open.
