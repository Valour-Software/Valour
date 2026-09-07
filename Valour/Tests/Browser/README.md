# Villages browser regression

These tests run real browsers against a local QA server. Chromium is the default;
the mobile and responsive suites also support WebKit. Use a
dedicated planet with the default village seed, named `Village Release QA` (or
set `VILLAGE_QA_PLANET`). The owner account must manage that village. The guest
account must already belong to the planet and must not own its Town Hall or
have ManageVillage permission. The scripts reject non-local server URLs.

Install Playwright in your test environment and either install its Chromium
browser or provide `BROWSER_EXECUTABLE`. If Playwright is installed outside this
repository, point `PLAYWRIGHT_MODULE` to its `index.mjs`.

```sh
export VILLAGE_QA_URL=http://localhost:5100
export VILLAGE_QA_EMAIL=owner@local.test
export VILLAGE_QA_PASSWORD='your local test password'
export VILLAGE_QA_GUEST_EMAIL=guest@local.test
export VILLAGE_QA_GUEST_PASSWORD='your local guest password'
node Valour/Tests/Browser/villages-rendering.mjs
python3 Tools/Villages/verify-library.py --interiors /path/to/moderninteriors-win \
  --reference-dir TestResults/village-browser/asset-review/references
node Valour/Tests/Browser/villages-library.mjs
node Valour/Tests/Browser/villages-release.mjs
node Valour/Tests/Browser/villages-presence.mjs
node Valour/Tests/Browser/villages-mobile.mjs
node Valour/Tests/Browser/villages-responsive.mjs
node Valour/Tests/Browser/villages-tileset-touch.mjs
node Valour/Tests/Browser/villages-navigation-touch.mjs
node Valour/Tests/Browser/sidebar-lifecycle.mjs
dotnet build Valour/BuildTools/CssBundler/CssBundler.csproj
node Valour/Tests/Browser/villages-css-bundle.mjs
```

The rendering suite serves only local fixture files and needs no account or
server. It uses the application renderer to check wall seams and captures a
complete furnished office plus every wall style at corners and junctions.
The library suite uses those private references to compare all 170 objects in
the application renderer at four scales (680 full-image comparisons). It also
uses the real tileset editor runtime to accept the complete glass cabinet and
reject the cropped-header and out-of-bounds regressions. Source references,
contact sheets and generated atlases stay outside Git. The desktop suite places,
persists and erases both bookcases, the glass cabinet, all three desks and
kitchen/music furnishings through the builder, and checks their categories.

Run the server-connected scripts sequentially; a user's village presence is shared across that
user's open connections. Each script writes screenshots and JSON results under
`TestResults/village-browser`, or `VILLAGE_QA_OUTPUT` if supplied. The building
tests remove the objects they create and restore the tested floor cells.
The presence test sends a short QA message in the temporary outdoor room.

The desktop suite covers login, Places, office/house entry, zoom, connected room
perimeters, doorway erasure, catalog search, furniture placement and movement,
painting, persistence and camera panning. The presence suite uses separate
browser contexts and accounts, verifies remote names and chat text actually
reach the canvas, checks movement notifications and room-scoped occupancy,
checks guest edit rejection, and verifies disconnect cleanup. The mobile suite
uses an iPhone viewport and touch input in Chromium or WebKit to exercise sidebar
navigation, handheld movement, touch furnishing, exit and control switching.
The desktop suite also creates an outdoor building, enters its furnished home,
and archives the test building during cleanup.
The presence suite also grants a temporary manager role, checks that the open
village enables building, deletes the assigned role while its editor is open,
and checks both UI revocation and server rejection after a replacement role
reuses the same bit. Temporary roles are deleted during cleanup.

## Screen sizes and touch editing

`villages-responsive.mjs` covers 15 viewports: touch layouts at 320×568,
375×667, 390×844, 430×932, 568×320, 667×375, 844×390, 932×430, 768×1024 and
1024×768, plus desktop layouts at 650×800, 850×900, 1280×800, 1440×900 and
1920×1080. Desktop panes retain the application sidebar, so the narrowest village
and tileset panes are 420 pixels wide. It checks bounds and overlapping overlays,
opens Places and building details, enters/leaves a house, searches and selects
furniture, reaches the last category and all six build tools, and checks handheld
controls. Staff template controls and tileset forms/exports must remain reachable.
Touch profiles use taps for navigation and controls, including the staff menu;
desktop profiles use mouse clicks. The matrix does not publish a template or
save any tileset changes.

The responsive and mobile suites also accept `VILLAGE_QA_BROWSER=webkit` after
installing Playwright's WebKit browser. Set `VILLAGE_QA_OUTPUT` to keep evidence
from each engine separately. These are browser emulations, not physical iPhone
or Android device tests. `VILLAGE_QA_GROUP=touch` or `desktop` restricts the matrix
when investigating a failure.

`villages-tileset-touch.mjs` serves the actual editor module and private atlas in
a local browser fixture. Real touch events verify rectangle selection, pinch
zoom, two-finger panning, cancellation and release outside the canvas; mouse
selection must still work. It needs no account or server. The CSS bundler suite
builds a temporary stylesheet with combined and nested container conditions and
checks their actual computed styles above and below the breakpoint. It also
checks quoted values and strings and deterministic output. This catches release
minification that silently turns valid responsive rules into invalid CSS.

`villages-navigation-touch.mjs` exercises the actual sidebar module with touch
events: edge swipes, canvas/button isolation, vertical scrolling, multi-finger
cancellation, rotation and disposal. It uses a local fixture and no account.
Native buttons in the application navigation are essential: a mouse click in a
mobile viewport can pass while a WebKit touch on a delegated non-button fails.

`sidebar-lifecycle.mjs` holds the sidebar module import while an internal route
change removes the app view. Returning must create a working sidebar, and five
Village close/reopen cycles must leave no disposed-reference or rendering errors.
It supports Chromium and WebKit and checks console rendering errors as well as
uncaught page errors. `SidebarLifecycleTests` additionally schedules disposal
during storage, import and instance initialization and verifies reference cleanup.

## Local media regression

With an isolated LiveKit server running on localhost, configure the QA server's
`Voice` section with `Provider: livekit`, `LiveKitUrl: ws://localhost:7880`,
`LiveKitApiUrl: http://localhost:7880`, and the matching `LiveKitApiKey` and
`LiveKitApiSecret`. Use development credentials, never production credentials.
The instance manifest must advertise that local endpoint. Then run:

```sh
node Valour/Tests/Browser/villages-media.mjs
VILLAGE_QA_MEDIA_MOBILE=1 VILLAGE_QA_OUTPUT=TestResults/village-browser/media-mobile \
  node Valour/Tests/Browser/villages-media.mjs
```

This uses the same two QA accounts and rejects remote SFU endpoints. Chromium
supplies synthetic microphone and camera devices. Screen acquisition supplies
an animated canvas stream, so no desktop or personal window is captured. The
application's SDK, server token routes, SFU, WebRTC and video elements are real.
Assertions cover received audio packets and decoded camera frames, remote mute
state, spatial audio routing, screen sharing in grid/focus/full-screen views
(including recovery to the full 640 × 360 source resolution),
room transitions, the last participant's waiting state, and capture/connection
cleanup. Results include media byte/frame counts and screenshots.

The mobile variant gives the guest touch input and checks mute/unmute, place
details during a call, and 20 expanded/minimized call layouts across 375×667,
390×844, 568×320, 844×390 and 768×1024 with both control modes. It checks panel
bounds, overlaps and every handheld button's hit target while media is active.
Run the browser media suite separately from the C# live-provider suite: provider
tests and their background cleanup services must not share an active SFU with
browser participants.

The local QA configuration must use a disposable PostgreSQL database and Redis
database. Never run this suite against a personal planet with valuable edits.

## Isolated C# regression

Build the test project, then use the local-only runner with a private QA config:

```sh
dotnet build Valour/Tests/Valour.Tests.csproj
python3 Tools/Villages/run-local-tests.py --config /private/path/appsettings.json
python3 Tools/Villages/run-local-tests.py --config /private/path/appsettings.json --full
```

The runner rejects non-loopback database and Redis endpoints, requires a dedicated
test/QA database name, copies all binaries and configuration to a temporary
directory, explicitly overrides data-service environment settings, and disables
Sentry and uses temporary filesystem media storage. Its default filter runs
Villages and ID generation regressions; `--filter`
selects other tests and `--serial` serializes test collections if needed.

`ConfigLoader` resolves `appsettings.json` beside the executable. Changing only
the working directory does not isolate a server. **Do not symlink test binaries:**
xUnit v3 launches the native `Valour.Tests` apphost, which can resolve settings
beside the original binary even when the test DLL was copied. For a published
server, replace the published `appsettings.json` with the private local config
after every publish and before starting it. Confirm both hosts use the intended
local data services. `TEST_DB_*` variables alone do not override Redis.

Real-device microphone/camera/screen-share interoperability remains a separate
release check. The synthetic local media suite does not substitute for a
two-person call through the configured production media provider or an iOS
Safari device test.

## Default template and doorway regression

`villages-atlas-protection.mjs` uses the same local staff account to verify the
published binary, the missing readable PNG endpoint, decoded byte integrity,
desktop and touch-phone furniture previews, interiors and staff library exports.
Run it in Chromium and with `BROWSER_ENGINE=webkit`. It writes private evidence to
`TestResults/village-browser/atlas-protection-<engine>`. The library pixel suite
also decodes the protected atlas before comparing whole objects with their source
references, and supports the same browser-engine setting. Build first to generate
`library-atlas.vtex.bin`.

Run `villages-template.mjs` with the same local account/environment variables as
the release suite. The account must be staff and manage the review village. This
suite publishes that local village as the shared draft, opens it through Edit &
preview, then checks source-sheet and named-asset navigation and exports/imports
its packed atlas. It checks each exported PNG fingerprint and compares every
browser-rendered pixel, including alpha, after the round trip; PNG encoders can
normalize invisible RGB and semi-transparent channel rounding. Footprints,
placement layers and supporting surfaces must also survive. Evidence is written under
`TestResults/village-browser/template`.

Use `Tools/Villages/run-local-server.py --config /private/path/appsettings.json
--publish` for the review server. Stop another review server before invoking
it. It validates local endpoints before publishing, rejects inherited configuration
overrides, and restores the local config in a finally block before any startup.
`--publish-only` builds without starting.
