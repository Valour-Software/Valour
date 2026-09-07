# Village release verification

Use this checklist to verify the village implementation and its release assets.
Record results for the exact checkout and build being tested. Test counts and
screenshots from another build do not establish that this build passes.

## Prepare an isolated environment

Use a disposable local PostgreSQL database, local Redis, and dedicated staff/guest
QA accounts. The staff account must manage the review planet; the guest must be a
member without Manage Village or ownership of the property used for access checks.
The browser scripts accept local server URLs only.

Build the test project and run the isolated helper:

```sh
dotnet build Valour/Tests/Valour.Tests.csproj
python3 Tools/Villages/run-local-tests.py --config /private/path/appsettings.json
python3 Tools/Villages/run-local-tests.py --config /private/path/appsettings.json --full
node --test Valour/Tests/Js/*.test.mjs
python3 Tools/Villages/test_library_art.py
python3 Tools/Villages/test_default_world_export.py
```

The helper copies binaries and native apphosts, validates local data endpoints,
uses temporary filesystem media, and disables error reporting. Configuration is
resolved beside the executable, so changing the working directory or symlinking
a native apphost does not isolate a test process. `TEST_DB_*` settings alone do not
isolate Redis.

For a local Release review server, restore the matching private atlas first, then
use `Tools/Villages/run-local-server.py --config /private/path/appsettings.json
--publish`. Stop another review server before starting it. The helper validates
configuration and restores the local settings after publishing. `--publish-only`
prepares the output without starting a server.

## Verify artwork and packaging

Follow [Village tilesets](VillageTilesets.md) to regenerate or restore the manifest
and atlas. Keep purchased source art and the generated atlas in private storage.
The manifest's `imageSha256` must match the PNG, and its image URL must address
the protected binary generated from that exact PNG. Release builds check local
atlas presence, dimensions and fingerprints. Verify that the published output
contains `library-atlas.vtex.bin` and does not expose the readable PNG. Run
`villages-atlas-protection.mjs` in both browser engines to check decoding,
previews and staff exports through the served app.

Run the source verifier and inspect every contact-sheet page. The library browser
suite compares whole objects with private source references at several scales.
The renderer suite checks a furnished office and all wall styles at corners,
junctions, and filled blocks. Tileset export/import must preserve rendered pixels,
footprints, placement layers, support flags, and the manifest fingerprint.

## Verify world behavior

Run the server-connected browser suites sequentially using the setup in
[the browser test guide](../Valour/Tests/Browser/README.md). They share account
presence and should not compete for the same session.

| Area | Required checks |
| --- | --- |
| Navigation | Open and close the village, change planets, inspect Places, and restore the sidebar after a route change |
| Movement | Walk onto authored porches, enter and exit each building, and reconnect without losing valid presence |
| Building | Place, move, paint, erase, pan, and create a building with a reachable furnished interior |
| Permissions | Reject guest edits, apply a role grant live, revoke it live, and deny access after its bit is reused |
| Presence and text | Render remote names and bubbles, keep occupancy scoped to the room, and clear disconnected members |
| Templates | Reject invalid arrivals and interiors, freeze publication, remap copied IDs/channels, and limit resets to village data |
| Bundled default | Install the embedded JSON on a fresh database, preserve staff publications and draft/reset state on upgrade and rollback, and clone every room with new IDs |
| Persistence | Reload scenes after edits and confirm objects, ownership, and collision agree |

The tests that publish or reset templates change the shared template state in the
local QA database. Use a disposable environment even when the browser script
cleans up the individual objects it creates.

## Verify layouts and input

Run the responsive matrix in Chromium and WebKit. It covers phone portrait and
landscape, tablet sizes, and desktop panes with the application sidebar present.
Check that furniture categories, all build tools, template controls, and tileset
exports remain reachable without overlapping the map's essential controls.

Use actual touch events for the mobile suites. Check floating-stick and handheld
movement, selection, two-finger pan/zoom, cancellation, and release outside the
canvas. Run the CSS bundle fixture so container rules are checked after Release
minification, and the sidebar lifecycle suite so delayed initialization cannot
reactivate a disposed component.

## Verify media

Run the local media suite with an isolated LiveKit server and both QA accounts.
The suite uses real SDK, server, SFU, and WebRTC paths with synthetic microphone,
camera, and screen streams. It checks received packets/frames, mute state, spatial
routing, screen-share resolution in grid/focus/full-screen views, room changes,
waiting state, and capture cleanup. The mobile variant also checks call layout
and movement controls together.

Run provider tests separately from browser calls so their cleanup workers do not
share active provider rooms. Local synthetic calls cover the application paths;
release checks also need a two-person call through the intended provider and
network. Check microphone, camera, screen sharing, reconnect, room transitions,
and app closure on physical iOS and Android devices. Measure expected participant
capacity separately from the two-account functional tests.

## Record and review results

Keep screenshots, JSON results, and TRX reports under `TestResults/` or a private
release artifact. Record the commit, working-tree changes, build configuration,
browser engines, test filters, and data-service configuration without credentials.
Report skipped tests separately from passes and retain failures with their logs.

Before deployment, verify that the served manifest and PNG match the release pair,
that the client can load the atlas, and that the PWA updates its assets. Review
[the world architecture](VillageArchitecture.md) for rendering and presence
constraints when assessing failures.
