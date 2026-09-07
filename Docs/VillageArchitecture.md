# Village architecture

A village is a planet's shared 2D world. Members walk between outdoor spaces and
building interiors, talk to people nearby, and use buildings to open the planet's
chat and call spaces. Plots and buildings can belong to members, and authorized
members can furnish and edit the world.

## Responsibilities

The server stores the world, checks edits and movement, and distributes presence.
The SDK loads scenes and maintains model stores and the current presence session.
`VillageWindowComponent` manages the Blazor window, controls, inspectors, and call
integration. Its JavaScript runtime handles drawing, camera movement, input,
collision previews, and position interpolation.

Per-frame work stays in JavaScript. Blazor receives meaningful state changes such
as selection, room entry, or zoom readouts. The client keeps at most one village
window active, since `VillageService` owns one current-map presence session.
Opening another planet's village replaces the content of that window.

`Planet.EnableVillage` is enforced by HTTP routes and hub methods as well as the
navigation UI. Disabling a village updates the hosted planet, clears live presence,
retires temporary rooms, and tears down open client runtimes. Re-enabling it allows
an open window to reload from the planet update.

## Persistent world data

Village entities use the shared planet-model and SDK planet-model patterns:

| Entity | Responsibility |
| --- | --- |
| `VillageMap` | Outdoor map or interior, with bounds, spawn, and optional parent building |
| `VillageMapChunk` | A 32 by 32 block with tile-layer and collision data |
| `VillagePlot` | Land ownership, build boundaries, and sale terms |
| `VillageBuilding` | Footprint, entrance, interior link, optional channel, and voice mode |
| `VillageObject` | An individually placed furnishing, wall, or ground object |

Each entity has a cascading foreign key to its planet. Map, plot, building,
channel, and owner references are plain columns whose consistency is maintained
by the services. This includes the two-way relationship between an interior map
and its parent building.

Chunks divide map data into bounded pieces. The runtime draws ground from
negative-Z objects; it does not decode `VillageMapChunk.LayerData`. The collision
service does read chunk `CollisionData`: either a row-major 32 by 32 boolean array
or a JSON object such as `{ "blocked": [12, 13] }`. Malformed collision data blocks
movement through the affected chunk.

## Presence and reconnection

`VillagePresenceService` keeps positions in memory on the node hosting the planet.
Presence is per map and is not saved to the database. Clients announce themselves
when joining and rejoin after reconnection. Restarting the node therefore clears
presence until clients restore their sessions.

Map groups use `v-{planetId}-{mapId}`. Movement carries tile coordinates and facing;
names and avatars arrive on join or in presence snapshots. A movement update must
preserve the identity already known for that member. Characters render as member
avatar tokens; facing is carried in the protocol but does not select directional
character artwork.

Within a map, the server permits one cardinal tile per move and checks collision.
A portal transition joins a different map. Invalid jumps are rejected before they
can alter or throttle legitimate presence. Presence records track their owning
connection ID, so a delayed disconnect from another socket cannot remove a
restored session.

The SDK rejoins at the last local tile after node authentication. If rejoin fails,
the window can retain its map as context, but nearby communication and temporary
room acquisition stay unavailable until a successful retry. Walking through a door
changes both the map and its real-time group.

## Doors, interiors, and collision

Tileset masks use named states. The editor authors `empty`, `solid`, and `door`.
Door cells are walkable entrances; unknown states retain their names and block
movement. A sprite with a door inside its footprint creates a building and an
interior when placed. A building-shaped sprite without a door is scenery.

Outdoor entrance cells lead directly to the building's interior. Each interior
has an exit portal on its spawn tile leading back to the entrance. Multiple authored
door cells remain walkable, and the lowest door cell supplies the return target.
The persisted map determines building context, which the server does not trust the
client to choose independently.

`VillageCollisionService` builds an immutable map snapshot from bounds, object
masks, building footprints, door states, and chunk collision. Joins warm the cache;
movement uses in-memory lookups. After saving a collision-affecting edit, services
invalidate the map snapshot before subsequent movement uses it.

Furniture uses bottom-aligned ground footprints, allowing a tree canopy or tall
sprite to overhang walkable cells. Drawing, selection, and collision use the same
geometry rules. Movement updates after a map transition use the runtime's actual
arrival tile so its next step agrees with the server's position.

## Temporary rooms and voice

Buildings select a `VillageVoiceMode`:

| Mode | Behavior |
| --- | --- |
| `None` | No voice room |
| `LinkedChannel` | Use the building's linked planet voice channel |
| `AutoRoom` | Lease a temporary video-capable channel and its associated chat |

Outdoor maps use temporary rooms keyed by map ID. Building rooms use a distinct
building scope. Map-room requests are allowed only for maps without a parent
building, and every acquisition requires matching live presence. The window awaits
movement/context updates before acquiring the destination room.

Occupants share a lease. Twenty seconds after the last occupant leaves, the channel
is soft-deleted; reacquiring it cancels that cleanup. Disconnects release leases,
and room acquisition after restart removes orphaned temporary channels. A restored
village reacquires its room after presence rejoins and follows a replacement
channel if an active village call needs one. Rebinding a building retires its room.

Temporary channel names begin with `◇ ` and are filtered out of the ordinary channel
directory. Members use them through the village's call controls and nearby composer.
The underlying chat and call services still enforce their normal permissions.

Auto-join is an opt-in setting for the session. With it off, entering another voice
space leaves the current call and offers a join button. `GlobalCallSessionService`
has no start-muted option, so automatically joining could open a microphone. The
call layer handles one channel at a time, and its minimum participant count of two
keeps a lone member in the waiting state without an SFU connection.

`CallPanelComponent` uses its nearby layout inside villages. Other participants'
avatars form a translucent vertical list centered on the right. Speaking avatars
become opaque. With spatial sound enabled, the list includes people on the current
map within 16 tiles; with it disabled, it includes everyone else in the call.
Camera and screen-share buttons appear below an avatar when the corresponding
track is available. Selecting one opens a small video view beside the list. The
view closes when that track disappears or the person leaves hearing range.

The list does not reserve space for an empty call. An options button exposes call
controls, while audio hosts stay mounted independently of the visible avatars and
video. LiveKit video hosts use track `attach()` and `detach()` so stream adaptation
follows the displayed size. Removing one host must not stop a track still
displayed in another view.

## Nearby text and spatial audio

The nearby composer uses the outdoor room's associated chat, with the planet's
default chat as a fallback. Inside a building it uses the building's chat context,
including the associated chat for voice/video or temporary rooms. The village
holds its own keyed real-time channel subscription and releases it on room changes
and disposal.

A message becomes a canvas bubble only when its author is present on the same map
and its channel matches the spatial context. Bubbles use plain text produced by
the safe markdown pipeline. Each speaker has at most four bubbles; each is wrapped
to three lines, held for seven seconds, and then faded. The channel retains the
message history. The send response and real-time delivery share the confirmed
message ID, so either can display the bubble without adding a duplicate.

Spatial sound is enabled by default. Each remote microphone uses a Web Audio graph:

```text
MediaStreamSource -> PannerNode -> distance GainNode -> output GainNode
```

The listener stays at the origin, with other participants positioned relative to
it. HRTF panning provides direction. A reverse-smoothstep distance curve keeps full
volume through two tiles and reaches silence at sixteen tiles. Position and gain
changes are ramped to avoid clicks. Output gain preserves the participant volume
chosen in the normal call controls.

The original audio element remains attached. It is muted only after the spatial
graph can route its stream; disabling spatial mode restores the original audio.
Participants outside the current map are muted while spatial mode is active.
Positions come from eased render positions each frame so sound follows the visible
characters. Presence and voice-peer arrays cross .NET interop as single arguments.

## Ownership, sales, and property settings

Owners can list, update, or withdraw their property from sale. Members with
`ManageVillage` can manage community property. Purchases require membership and
economy-send authority. The planet sells unowned property into a shared account;
without a configured currency, starter property can be claimed for free.

Every purchase opens a confirmation modal showing the price. Property model
changes refresh open scenes with a half-second debounce while preserving selection.
Owners can rename and describe their property. Managers can also change or clear
a building's linked channel. Clearing the link gives it leased area rooms.

Payment and ownership transfer use separate commits. `EcoService` commits the
payment first, then the village service transfers the deed. A transaction
fingerprint derived from the sale prevents a retry from charging twice. Each new
listing gets a persisted sale ID, so selling the same property to the same person
again creates a separate payment. Listing changes and purchases share a per-asset
lock on the planet's hosting node.

## Building and editing

The build catalog comes from the embedded tileset definitions used by server
collision checks. Owners can edit within their outdoor plots and throughout their
building interiors. `ManageVillage` permits whole-map editing. Every server
mutation repeats authorization and bounds checks; a green preview is only feedback.

The catalog includes authored sprites and a Buildings category. Placing a sprite
with a door creates its building and furnished interior in one save. Erasing a
building archives it and its interior, retaining furnishings in archived data while
removing the building from ordinary navigation and queries.

Furnishings have ground footprints and placement layers. Rugs sit above painted
ground and beneath furniture. Surface accessories need a supporting item across
their full footprint and render at tabletop height. Supporting furniture cannot
be moved or removed until its accessories have been moved. Move preserves the
object's ID and requires permission at both ends. Blocking edits cannot occupy a
member's current tile.

Paint uses logical terrain brushes. The server resolves brush keys to authorized
terrain definitions. Mouse and pen strokes interpolate skipped cells, deduplicate
locally, and preview changes immediately. Pointer-up submits the complete cell
list in one request. A rejected request or cancelled gesture restores the untouched
scene snapshot; success replaces the preview with the authoritative result.
Shift-drag fills a rectangular area using the same batch operation.

Manual brushes repeat the selected pattern with map-coordinate alignment. Manual
ground uses Z index -101; automatic terrain uses -100. Replacing automatic terrain
with manual art repairs neighboring automatic edges once and leaves the manual
cells under manual control. Rugs survive painting and rollback.

Walls use continuous strokes, with Shift-drag outlining an empty room perimeter.
They are blocking positive-Z objects keyed as `wall:{set}:{frame}`. The server
requires both cardinal neighbors before accepting a diagonal connection, resolves
the eight-neighbor mask to 47 shapes, and updates changed cells plus neighbors
after paint or erase. Different styles connect while retaining their own art.
`Blob47` and `RoomBuilder` layouts share topology but compose their textures
differently. See [Village tilesets](VillageTilesets.md).

Pan or Space-drag moves the camera during editing. Build mode pauses movement keys,
deduplicates releases, and cancels incomplete gestures. Leaving build mode or
changing maps resets the camera. Placement is serialized per map and rejects
unknown definitions, inaccessible cells, incompatible overlaps, and blocked doors.

## Rendering and camera

The runtime resolves definition keys to atlas rectangles, culls offscreen art,
and sorts props, buildings, and characters by their bottom edge. Negative-Z ground
objects render first. Missing artwork uses geometric fallbacks so the map remains
navigable.

Static ground is composited into an offscreen canvas for each map and scale. The
cache is invalidated by scene changes, scale changes, or newly loaded textures.
Large maps that exceed the canvas area limit use culled drawing instead. Texture
callbacks are registered only for loads still in progress; registering a redraw
callback on every lookup can create an endless sequence of microtasks.

The camera centers the player in the visible strip between the top controls and
bottom composer. It can extend beyond a map edge to keep the player out from under
those panels, drawing background outside the map. Rendering and culling therefore
support negative camera positions.

Toolbar zoom uses 25-percent steps, while wheel and pinch input adjust a continuous
target between 50 and 200 percent. Both ease in the frame loop. The static composite
is scaled during the animation and rebuilt when zoom settles. Whole-percent HUD
updates are limited to once every 100 milliseconds. Interiors use a closer baseline
scale than outdoor maps.

## Terrain rules

Tilesets name terrain materials and assign each a priority. Definitions identify
base tiles and transition roles such as Edge, Corner, and InnerCorner. Base variants
use a per-cell hash and optional weights, so rebuilding a static layer does not
randomly change the ground's appearance.

The resolver reads each cell's eight neighbors. A transition direction names the
side containing the other material, and `Against` can restrict art to a particular
neighbor. Exactly one side draws a boundary: matching authored art wins over absent
art, a specific material pair wins over a wildcard, then priority and the terrain
key break ties. Missing transition art falls back to a base tile.

The map editor stores material keys in its terrain grid and resolves the affected
art when painting. A manually painted tile removes that cell from the terrain grid;
unpainted and out-of-bounds cells do not create terrain fringes. Exported map JSON
contains both the resolved tile layer and the terrain grid. Terrain fields are
separate from `GroupKey`, which categorizes definitions, and `Direction`, which
describes the artwork's facing.

## Touch and responsive layouts

The default touch control is a floating stick anchored where the finger lands.
Crossing its deadzone steers; a short tap inspects a building. A resting joystick
indicator gives touch users a visible starting point. Two fingers switch the
gesture to pinch zoom, and lifting a finger ends the gesture without resuming
movement until another touch starts.

Valour Pocket is a device preference that replaces drag-steering with handheld
controls. The D-pad uses the keyboard movement loop, A selects or enters a nearby
place, B closes a surface or exits an interior, Start opens Places, and Select
focuses nearby chat. Build and staff tools hide the handheld controls to make room
for editing.

Pointer capture keeps gestures active outside the canvas; cancellation restores
an unfinished edit. The canvas uses `touch-action: none`. Responsive layout is based
on available width and height, while the client's mobile device flag is set from
the user agent. Narrowing a desktop browser changes layout without making it a
mobile device.

The furnishing catalog becomes a bottom sheet on narrow portrait panes and a
compact sidebar on short landscape panes. Call, chat, and movement controls share
the remaining space. Places and details can temporarily hide the call surface
while media continues. Staff tileset forms stack below the artwork, and opening
staff tools closes the mobile sidebar.

## Lifecycle and permissions

An open village observes local role membership and the planet role store. A scene
refresh updates access and closes edit/property controls after permission loss.
Role deletion refreshes hosted members and connected channel access before a
replacement role can reuse the membership bit.

The runtime owns an animation loop and window key listeners. Call its JavaScript
`dispose()` before releasing interop references. Capture movement keys only when
the canvas is visible and focus is outside text input; always process key release
so a focus change cannot leave movement stuck.

Use a `ResizeObserver` and runtime size checks to keep canvas backing dimensions
aligned with layout. Editors without a frame loop also check size before pointer
hit tests and wheel handling. Snowflake IDs cross JavaScript boundaries as strings
because their values can exceed JavaScript's exact integer range. Scene callbacks
parse those strings on the .NET side.

The window inherits `ControlledRenderComponentBase`, so state-changing handlers
must call `ReRender()`. Explicit scene fetches bypass the node's short HTTP cache,
as do forced channel refreshes needed after a temporary room is retired.

## Default world and verification

New villages clone the published staff template with fresh IDs and destination
channel links. The versioned `Database/Seeds/Villages/default-world-v1.json` is
embedded in the database assembly and installed automatically by a data migration;
the same JSON is used when there is no publication. Existing staff publications
take precedence. The built-in world is a 52 by 40 landscaped
commons with Town Hall chat, voice and video venues, a Maker House, a claimable
parcel, and furnished interiors. Publication and reset rules are described in
[Staff default template](VillageDefaultTemplate.md).

Service tests cover presence, collision, sales, templates, and permission rules.
`VillageWorldApiLiveTests` checks persistence, reachable doors and exits, and
property-management authorization. JavaScript tests cover drawing helpers,
terrain/wall resolution, input, bubbles, and spatial audio. Browser suites exercise
the actual renderer and two-account presence, permissions, building, and media.
Use [Release verification](VillageReleaseQA.md) and the linked test guides to run
those checks in an isolated environment.
