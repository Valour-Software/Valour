# Valour Village Architecture

This document describes the proposed architecture for Valour Villages: a planet-scoped 2D social game runtime embedded inside the existing Valour client.

Villages are not a simple map view or decorative window type. They are a lightweight game subsystem that integrates with Valour planets, channels, voice, permissions, realtime sync, and future creator economies.

## Overview

Valour Villages should be built as a dedicated vertical slice with its own:

1. Shared models and requests
2. Database entities
3. Server services
4. Dynamic API routes
5. Client SDK models and services
6. Client game runtime and docked window

The feature must support:

- Outdoor village maps
- Interior maps
- Buildings linked to chat or voice channels
- Plot ownership and editing
- Furniture and decorative objects
- Realtime player presence
- Custom asset authoring from day one
- Layered user characters
- Future community sharing and selling of asset packs

The initial release does not need directional character rendering, but the architecture must support it later without requiring schema or pipeline rewrites.

## Goals

- Fit cleanly into the current planet-scoped reactive model system
- Keep village logic isolated from `PlanetService` and unrelated core services
- Support large communities and future map growth
- Separate persistent world state from ephemeral gameplay state
- Support official assets first while remaining marketplace-ready
- Support layered character composition instead of single baked sprite sheets

## Non-Goals For Initial Release

- Deep NPC simulation
- Farming, combat, or physics-heavy gameplay
- Public marketplace activation
- External asset activation for live communities
- Required directional art for all character parts
- Per-frame Blazor-driven game rendering

## Architectural Principles

### 1. Villages are a planet-scoped subsystem

Village state belongs to a planet, but it should not be modeled as miscellaneous planet metadata. New village entities should follow the existing planet-owned model pattern used elsewhere in Valour.

This aligns with:

- `ISharedPlanetModel`
- `ClientPlanetModel`
- `CoreHubService.NotifyPlanetItemChange`

### 2. Persistent and ephemeral state must be split

Persistent authoritative state:

- Maps
- Chunks and layers
- Plots
- Buildings
- Interiors
- Furniture and decor
- Asset definitions and pack metadata
- Character appearance selections

Ephemeral realtime state:

- Player positions
- Facing and animation state
- Current map occupancy
- Temporary emotes or effects

This split avoids storing high-frequency gameplay updates in heavyweight persistent models.

### 3. The renderer is a game runtime, not standard UI state

The village window should be a Blazor shell that hosts a dedicated TypeScript canvas runtime.

Blazor owns:

- Window lifecycle
- Toolbars and inspectors
- Modal workflows
- Permissions-aware editing controls
- Channel and voice integrations
- Save and load triggers

TypeScript owns:

- Render loop
- Camera
- Input handling
- Animation timing
- Collision and hit-testing
- Indoor and outdoor transitions
- Runtime character composition

### 4. Asset references must be logical, not raw file based

Maps, characters, and objects should reference stable asset identifiers and versions, not direct CDN paths. This supports:

- Version pinning
- Moderation and takedowns
- Fallback assets
- Theme swaps
- Future marketplace entitlements

## High-Level Architecture

```text
SERVER
Database Entities
  -> Village Services
  -> Asset Services
  -> Character Services
  -> Dynamic API Routes
  -> CoreHubService Realtime Broadcasts

CLIENT SDK
Node / SignalR
  -> Village Models
  -> Village Services
  -> Presence State
  -> Asset Resolution

CLIENT UI
Dock Window Shell
  -> Canvas Game Runtime
  -> Inspectors / Editors
  -> Channel / Voice Integrations
```

## Project Structure

Recommended structure:

```text
Valour/
├── Docs/
│   └── VillageArchitecture.md
├── Valour/
│   ├── Shared/
│   │   ├── Villages/
│   │   ├── VillageAssets/
│   │   └── Characters/
│   ├── Database/
│   │   └── Village*.cs
│   ├── Server/
│   │   ├── Api/Dynamic/
│   │   │   └── VillageApi.cs
│   │   └── Services/
│   │       ├── Villages/
│   │       ├── VillageAssets/
│   │       └── Characters/
│   ├── Sdk/
│   │   ├── Models/Villages/
│   │   ├── Models/VillageAssets/
│   │   ├── Models/Characters/
│   │   └── Services/Villages/
│   └── Client/
│       └── Components/Windows/Villages/
│           ├── VillageWindowComponent.razor
│           ├── VillageWindowComponent.razor.ts
│           └── Game/
```

## Core Domain Areas

### Villages

Owns world structure and gameplay space:

- Outdoor maps
- Interior maps
- Plot ownership
- Buildings
- Furniture and decor
- Portals

### Village Assets

Owns the visual and audio content pipeline:

- Asset packs
- Tilesets
- Sprite sheets
- Character parts
- Metadata definitions
- Validation and publishing state

### Characters

Owns user avatars in the village runtime:

- Layered appearance
- Rig compatibility
- Future directionality support
- Cosmetic resolution

### Village Presence

Owns map-local ephemeral presence:

- Join and leave map
- Movement updates
- Facing and animation state
- Occupant snapshots

## Data Model

### Persistent World Models

#### `VillageMap`

Represents either an outdoor map or an interior map.

Suggested fields:

- `Id`
- `PlanetId`
- `MapType` (`Outdoor`, `Interior`)
- `Name`
- `ParentBuildingId` nullable
- `Width`
- `Height`
- `ChunkWidth`
- `ChunkHeight`
- `ActiveAssetPackId`
- `DefaultSpawnPoint`
- `MusicAssetId` nullable
- `Version`

Notes:

- A planet can have one primary outdoor map and many interior maps
- Interiors are first-class maps, not special cases

#### `VillageMapChunk`

Stores chunked map content for scalable persistence.

Suggested fields:

- `Id`
- `MapId`
- `ChunkX`
- `ChunkY`
- `Version`
- `LayerData`
- `CollisionData`
- `MetadataData`

Notes:

- Chunking should exist from day one
- Small interiors may still be stored as one chunk

#### `VillagePlot`

Represents a claimable or managed parcel of land.

Suggested fields:

- `Id`
- `PlanetId`
- `MapId`
- `OwnerMemberId` nullable
- `X`
- `Y`
- `Width`
- `Height`
- `PlotType`
- `PermissionsMode`
- `Name`

#### `VillageBuilding`

Represents a structure placed on a map.

Suggested fields:

- `Id`
- `PlanetId`
- `ExteriorMapId`
- `InteriorMapId` nullable
- `PlotId` nullable
- `AssetDefinitionId`
- `ChannelId` nullable
- `X`
- `Y`
- `Width`
- `Height`
- `Rotation`
- `OwnerMemberId` nullable
- `Name`

Notes:

- `ChannelId` allows direct integration with existing Valour channels
- `InteriorMapId` makes enterable buildings straightforward

#### `VillageObject`

Represents placed decor, furniture, or interactables.

Suggested fields:

- `Id`
- `PlanetId`
- `MapId`
- `AssetDefinitionId`
- `OwnerMemberId` nullable
- `X`
- `Y`
- `Rotation`
- `ZIndex`
- `ObjectStateData`

### Ephemeral Models

#### `VillagePresence`

Represents the live state of a player in the runtime.

Suggested fields:

- `PlanetId`
- `UserId`
- `MapId`
- `X`
- `Y`
- `FacingDirection`
- `AnimationState`
- `EmoteState`
- `Timestamp`

Notes:

- Presence should not be the source of truth for user appearance
- Presence updates should be throttled and broadcast as coarse realtime events

### Character Models

#### `VillageCharacterAppearance`

Represents a user’s selected look for villages.

Suggested fields:

- `UserId`
- `RigId`
- `BodyVariantId`
- `PrimaryPalette`
- `SecondaryPalette`
- `Version`

#### `VillageCharacterLayerSelection`

Represents one selected layer in the composed character.

Suggested fields:

- `AppearanceId`
- `Slot`
- `AssetDefinitionId`
- `ColorOverrides`
- `SortOrder`

Supported slots may include:

- Body
- Hair
- Eyes
- Top
- Bottom
- Shoes
- Accessory
- HeldItem
- Effect

Important rule:

Character appearance must store logical layer selections, not baked sprite sheets.

## Asset Platform

Village assets should be marketplace-ready even if external activation is disabled initially.

### Core Models

#### `VillageAssetPack`

Top-level pack that groups authored content.

Suggested fields:

- `Id`
- `OwnerUserId` nullable
- `OwnerPlanetId` nullable
- `Name`
- `Description`
- `Scope`
- `Status`
- `Version`

Possible statuses:

- `Draft`
- `Private`
- `PlanetOnly`
- `Approved`
- `Published`
- `Disabled`

#### `VillageAssetDefinition`

The logical asset unit referenced by maps and characters.

Suggested fields:

- `Id`
- `AssetPackId`
- `AssetType`
- `Key`
- `DisplayName`
- `DefinitionData`
- `Version`

Asset types may include:

- Tile
- Building
- Object
- CharacterPart
- CharacterRig
- Effect
- Audio

#### `VillageAssetMedia`

References uploaded source or derived media.

Suggested fields:

- `Id`
- `AssetDefinitionId`
- `MediaType`
- `CdnPath`
- `Width`
- `Height`
- `Hash`

### Asset Rules

- Runtime references use logical asset ids and versions
- Uploaded media is validated before activation
- Official and creator-authored content use the same pipeline
- Public activation can remain disabled by policy while authoring exists

### Future Commerce Readiness

The schema should leave room for:

- Ownership
- Licensing
- Entitlements
- Pricing
- Sharing
- Moderation
- Dependencies between packs or rigs

## Character System

Characters should use layered sprite composition.

### Why layered composition

- Supports customization without exploding sheet counts
- Supports cosmetics and creator content later
- Supports recoloring and variants
- Keeps source-of-truth stable as rendering improves

### Directionality

Initial release requirements:

- Store facing state in presence
- Allow assets with no directional support
- Render neutral or non-directional walking and idle animations

Future support requirements:

- Four-direction rendering
- Eight-direction rendering
- Mirrored variants where appropriate
- Layer-aware directional character composition

Character assets should declare metadata such as:

- `SupportsDirectionality`
- `DirectionCount`
- `CanMirror`
- `FrameLayout`
- `CompatibleRigId`

## Rendering Architecture

### Window Integration

Villages should be opened as a dedicated dock window content type.

Suggested components:

- `VillageWindowContent`
- `VillageWindowComponent.razor`
- `VillageWindowComponent.razor.ts`

The window should carry `PlanetId` so it participates in planet connection management in the same way as other planet-scoped windows.

### Runtime Responsibilities

The TypeScript runtime should manage:

- Render loop
- Asset resolution cache
- Camera and viewport
- Input and interaction
- Collision layers
- Character composition
- Map transitions
- Presence interpolation

### Why canvas

Canvas is the right initial target because it handles:

- Large tile scenes
- Layered rendering
- Camera pan and zoom
- Sprite batching
- Collision overlays and hit-testing
- Smooth animation without frequent Blazor rerenders

## Services

### Server Services

Recommended service groups:

#### `VillageMapService`

- Load maps
- Load chunks
- Save chunk updates
- Manage portals and spawn points

#### `VillageBuildingService`

- Place buildings
- Update links to channels
- Manage interior associations

#### `VillageInteriorService`

- Create and load interior maps
- Validate building to interior relationships

#### `VillageObjectService`

- Manage furniture and decor
- Validate placement and collisions

#### `VillagePresenceService`

- Track current players per map
- Broadcast join, leave, and move events
- Throttle updates

#### `VillagePermissionService`

- Validate build and edit rights
- Integrate with planet membership and future village permissions

#### `VillageAssetService`

- Resolve active asset packs
- Validate definitions and media
- Manage publishing state

#### `VillageCharacterService`

- Load and update character appearance
- Resolve layer selections and rigs

### SDK Services

Recommended client service groups:

- `VillageService`
- `VillagePresenceService`
- `VillageAssetService`
- `VillageCharacterService`

These may begin as one grouped `VillageService` namespace and split further as complexity grows.

## API Shape

Routes should remain map-centric and workflow-oriented.

Examples:

```text
GET    /api/planets/{planetId}/village
GET    /api/planets/{planetId}/village/maps/{mapId}
GET    /api/planets/{planetId}/village/maps/{mapId}/chunks
PUT    /api/planets/{planetId}/village/maps/{mapId}/chunks/{chunkX}/{chunkY}

POST   /api/planets/{planetId}/village/buildings
PUT    /api/planets/{planetId}/village/buildings/{buildingId}

POST   /api/planets/{planetId}/village/objects
PUT    /api/planets/{planetId}/village/objects/{objectId}

POST   /api/planets/{planetId}/village/interiors
GET    /api/planets/{planetId}/village/interiors/{mapId}

GET    /api/users/me/village-character
PUT    /api/users/me/village-character

POST   /api/planets/{planetId}/village/presence/join
POST   /api/planets/{planetId}/village/presence/move
POST   /api/planets/{planetId}/village/presence/leave
```

## Realtime Model

### Persistent model updates

Persistent village entities should use the existing planet-scoped realtime model pattern where practical. This keeps the implementation aligned with:

- `ClientPlanetModel`
- `ModelStore`
- `CoreHubService.NotifyPlanetItemChange`

Good candidates for standard realtime syncing:

- `VillageMap`
- `VillagePlot`
- `VillageBuilding`
- `VillageObject`

### Presence updates

Presence should use separate lightweight events rather than normal model persistence flows for every movement update.

Recommended event families:

- `VillagePresence-Snapshot`
- `VillagePresence-Joined`
- `VillagePresence-Left`
- `VillagePresence-Moved`
- `VillageMap-Changed`

Movement events should be throttled and possibly quantized to avoid flooding.

## Permissions

Village-specific permissions should eventually be introduced rather than overloading general planet management forever.

Suggested permissions:

- `ViewVillage`
- `WalkVillage`
- `ClaimPlot`
- `BuildStructures`
- `EditOwnInterior`
- `EditVillageMap`
- `ManageVillageAssets`
- `ManageVillage`

Early versions may map these checks onto existing planet management permissions, but the architecture should assume proper village permissions later.

## Integration With Existing Valour Features

### Planets

- Villages are owned by planets
- Village windows should hold `PlanetId`
- Planet connection lifecycle should govern village model subscriptions

### Channels

- Buildings may link to a chat or voice channel
- Entering or interacting with a building can open or focus the linked channel

### Voice

- Voice-linked buildings create a strong Gather-like social flow
- Future presence overlays can reflect voice occupancy or speaking state

### Roles and Membership

- Plot ownership and editing rights derive from planet membership
- Staff or builders can receive elevated village permissions

## Performance and Scale

### Chunking

Chunking is required from the first implementation for outdoor scalability.

### Presence throttling

Player movement updates should be throttled and interpolated client-side.

### Asset caching

Resolved asset packs, sprite sheets, and character layers should be cached aggressively on the client.

### Large communities

Future work may include:

- Region streaming
- District maps
- Occupancy-based interest management

The initial architecture should not block those later changes.

## Recommended MVP

First meaningful release:

1. One outdoor village map per planet
2. Enterable buildings with separate interior maps
3. Plot ownership
4. Building placement
5. Furniture or decor placement in interiors
6. Layered user characters
7. Realtime map presence
8. Building to channel mapping
9. Official assets only, but through the full asset platform pipeline

## Rollout Phases

### Phase 1: Foundation

- Shared village and asset contracts
- Core database entities
- Server and SDK service scaffolding
- Window shell and canvas runtime bootstrap

### Phase 2: World Runtime

- Outdoor maps
- Interiors
- Plot and building systems
- Basic object placement

### Phase 3: Presence and Characters

- Character appearance pipeline
- Layered sprite composition
- Realtime movement and occupancy
- Non-directional animation support

### Phase 4: Asset Authoring

- Asset definitions
- Upload and validation pipeline
- Draft and private pack workflows
- Admin-only or internal activation

### Phase 5: Creator Economy

- Sharing
- Marketplace entitlements
- Planet-scoped adoption of creator packs
- Moderation workflows

## Open Questions

- Should village presence stay inside `CoreHub`, or should a dedicated village event channel be introduced later?
- What chunk size best balances persistence cost and editing ergonomics?
- Which village permissions should map to current planet permissions during transition?
- Should buildings support multiple interaction targets beyond one primary `ChannelId`?
- How much of character composition should be cached client-side versus rendered every frame?

## Summary

Valour Villages should be built as a dedicated game subsystem that is:

- Planet-scoped
- Realtime-aware
- Canvas-driven
- Asset-platform-backed
- Marketplace-ready
- Character-layer-based

The most important early decisions are:

1. Treat interiors as first-class maps
2. Separate persistent world state from ephemeral presence state
3. Build a real asset platform even before public creator activation
4. Store character appearance as logical layered selections, not baked sheets
5. Design for future directionality while allowing non-directional art initially

