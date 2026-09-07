# Staff default village template

Open **User settings → Staff → Default Village**. This is the authoring
and publication screen for the world copied into new villages.

## Authoring and publishing

1. Create or choose a village that staff can manage. Open it once so its maps
   exist. A private staff planet makes a useful workshop.
2. Choose that planet under **Workshop village**, then select **Use as draft**.
   The selection is shared by staff; it is not a preference stored in one browser.
3. Use **Edit & preview draft** to open the regular village experience. Floors,
   connected walls, buildings, room furnishings and plots use the same editor
   and persistence rules as ordinary villages. Preview by closing build mode
   and walking through the rooms.
4. Return to Default Village and publish the next revision. The server checks
   map bounds, asset keys, arrivals, reachable building doors and connected
   interiors before capturing the draft. A blocked or disconnected world is
   rejected with the affected room or building's name.

The published template is an immutable snapshot. Later draft edits do not
change it or any worlds already created from it. A stale publish screen cannot
silently replace another staff member's newer publication or selected draft.
The screen shows the current revision, map count, publisher and publication time.

Staff status is required on every template endpoint. Selecting and publishing
also require ordinary membership and **Manage Village** in the source planet.
Share the workshop with other staff using its normal membership and role tools.
Publication does not grant staff membership or property rights in other planets.

## New worlds and resets

New worlds receive fresh map, plot, building and object IDs. Interior and parcel
links are remapped. Property ownership is cleared; sale listings get independent
identities. Linked chat, voice and video spaces bind to the destination planet's
channels of the corresponding type. The source planet's channel IDs, owners,
messages and presence are never carried into the copy. Without a destination
currency, starter listings are free.

**Also replace existing worlds** advances the reset revision. Each village below the reset revision
is replaced the next time it opens or refreshes its scene. This removes its
village maps, furnishings, plots, buildings and property ownership, including
archived village data. It keeps the planet, ordinary channels, messages, members
and economy. Temporary village rooms and presence are retired. The draft
workshop is excluded.

**Reset one village** replaces the selected village immediately using the current
published snapshot. The active draft cannot be reset. This is separate from
publishing and requires its own explicit checkbox in the staff screen.

The initial built-in template is revision 1: the reviewed landscaped commons,
four connected, distinct interiors, five plots and 2,343 placed objects and floor
tiles. Its complete layout is checked in as
[`default-world-v1.json`](../Valour/Database/Seeds/Villages/default-world-v1.json).
A world whose template revision is below the reset threshold is reset when
opened. Changing the bundled seed must not raise that threshold implicitly.

## Persistence and deployment

Migration `20260906210002_VillageTemplatePublishing` adds the singleton
`village_template` and `village_maps.template_revision`. The singleton stores the
shared draft planet, immutable published JSON, publication metadata and reset
threshold. All server nodes sharing the database see the same publication.

Migration `20260907120000_SeedVillageDefaultWorld` installs the checked-in JSON
into that singleton when no published template exists. The JSON is embedded in
`Valour.Database.dll`, so normal server startup (`Database.Migrate()`) installs it
without access to the development database, R2 or a working-directory file.
An existing staff publication is preserved. An unpublished draft keeps its planet,
revision and reset threshold. Reapplying the data operation is safe; rolling it
back only clears the matching bundled seed, never a staff publication or a village.

World creation/reset uses a planet advisory transaction lock. Template writes
use a separate advisory lock and revision check. Publication reads its source in
a repeatable-read transaction so the snapshot cannot mix two versions of a room.
Staff draft selection, publishing and individual resets enter the staff audit log.

The same bundled JSON is the runtime fallback when there is no publication row
or its JSON is empty (`NULL`). Ordinary new-building interiors still use their
room blueprints. Staff publication continues to override the bundled default.
Tileset artwork is a separate versioned library: editing the default world does
not require repacking or deploying sprites. New asset definitions must be shipped
with their matching atlas before a draft using those keys can be published.

Restore the private asset ZIP at the repository root before Release publish or
the Docker build. Its PNG is a build input under `Valour/Client/wwwroot`; the build
checks it and publishes a protected `library-atlas.vtex.bin` instead. The readable
PNG is excluded from the published app. R2 can hold the private ZIP for CI; the
app serves the generated binary through its own static asset URL. The JSON
migration uses the embedded world layout and needs no access to R2. See
[tileset packaging](VillageTilesets.md) for the complete asset workflow.

## Exporting a bundled default

Publish and review the workshop through the staff screen first. Export only the
`published_json` column from the intended database, then normalize the snapshot:

```sh
psql 'service=local_qa' -X -At -c 'SELECT published_json FROM village_template WHERE id = 1' > /private/tmp/published-world.json
python3 Tools/Villages/export-default-world.py /private/tmp/published-world.json /private/tmp/default-world.json
```

`service=local_qa` denotes an explicitly configured local PostgreSQL service.
The exporter accepts a file and does not connect to a database. It keeps layout
fields, replaces entity IDs with portable local references, drops ownership,
planet, sale and publication identities, and keeps only referenced channel types.
It sorts entries for reviewable diffs and refuses to overwrite an existing seed.
The runtime assigns new IDs and destination channel connections when cloning.

Bundled seed resources and their migration references are immutable. The data
migration loads its specific resource, while `CurrentJson` selects the runtime
default. Seed installation preserves staff publications and the reset threshold.
Staff publication and explicit reset controls govern changes to live worlds.

Run `VillageDefaultWorldTests` and `VillageTemplateServiceTests` through the local
test helper, plus `python3 Tools/Villages/test_default_world_export.py`. They check
walkability and asset references, portable exports, migration install/rollback,
publication preservation, and independent complete copies of every map. Preview
the resulting world in a fresh village with the matching atlas before release.

## Local QA

`Tools/Villages/run-local-tests.py` validates local PostgreSQL and Redis endpoints,
copies test binaries and native apphosts, isolates media storage and disables
error reporting before starting xUnit. Do not run the original test apphost with
the server's ordinary configuration.

`Tools/Villages/run-local-server.py --config /path/to/local/appsettings.json
--publish` publishes and starts a local review server. Stop an existing review
server first. The helper restores the verified local configuration after publish
and refuses to start if validation or copying fails. `--publish-only` builds and
restores configuration without starting the server.

The browser regression in `Valour/Tests/Browser/villages-template.mjs` physically
walks up the porch and repeats entry/exit, checks room rendering and pane sizes,
uses staff publishing controls, and exports/reimports the tileset package. It
requires a staff QA account with Manage Village in the local review planet and
is restricted to localhost.
