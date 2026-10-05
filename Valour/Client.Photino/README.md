# Desktop host (Photino)

`Valour.Client.Photino` runs the shared Razor UI in `Valour/Client/` inside a
[Photino](https://www.tryphotino.io/) window. Photino uses the operating
system's web view: WebKitGTK on Linux, WKWebView on macOS, and WebView2 on
Windows. This project exists to support Linux, which .NET MAUI does not target.

## Running

```sh
dotnet run --project Valour/Client.Photino
```

A Debug build uses the server in the `VALOUR_API_BASE` environment variable
when it is set, for example `VALOUR_API_BASE=http://localhost:5100`. Otherwise
it uses `https://api.valour.gg`.

To produce a self-contained Linux build:

```sh
dotnet publish Valour/Client.Photino -c Release -r linux-x64 --self-contained -o out/linux-x64
```

Linux machines need GTK 3, WebKitGTK 4.1, and libnotify. On Debian and Ubuntu
these are `libgtk-3-0t64 libwebkit2gtk-4.1-0 libnotify4`. On Fedora they are
`gtk3 webkit2gtk4.1 libnotify`.

## Host services

| Service | Implementation |
|---|---|
| `IAppStorage` | `FileAppStorage` keeps preferences in `$XDG_CONFIG_HOME/valour/preferences.json`, usually `~/.config/valour/`. |
| `IE2eeKeyStore` | `SecretServiceKeyStore` stores encryption keys in the desktop keyring (GNOME Keyring or KWallet) through libsecret. If no keyring can store items, or keys were already written to disk, it uses `~/.config/valour/keys/`, which only the current user can read. The choice is made once per launch. |
| `IPushNotificationService` | `DesktopNotificationService` shows desktop notifications for notifications that arrive while Valour is running. Nothing is delivered while the app is closed. |
| `IExternalAuthLauncher` | The shared `LoopbackExternalAuthLauncher` opens the provider in the default browser and receives the result on a loopback port. |

Error reports go to Sentry in the desktop (`valour-windows`) project, and the
`os` tag tells Linux apart from Windows. As in the other hosts, reporting is off
unless the user turns it on in settings. The host reads that preference from
`preferences.json` before starting the SDK, so startup crashes follow it too.

## Web view integration

The page is served from `app://localhost/`. `wwwroot/desktop-host.js` sends
links and `window.open` calls that leave the app to the host, which opens them
with `xdg-open` (or `open` on macOS), because the web view cannot hand
navigation to the system browser by itself.

File uploads are sent by the page itself with `XMLHttpRequest` so that they
can report progress, which makes them cross-origin requests from
`app://localhost`. The server's CORS policy allows that origin (see
[native client uploads](../../Docs/Deployment/README.md#native-client-uploads)).
A server without it rejects the upload before it starts.

WebKitGTK has no inspector window unless one is opened by hand, so the page
also sends console errors, console warnings, and uncaught exceptions to the
host. Debug builds print them to standard error as `[web error]` and
`[web warn]` lines. Release builds ignore them.

WebKitGTK crashes its page process when a worker draws to a canvas that was
transferred with `transferControlToOffscreen` and the frame is committed
through the GPU canvas backend. The host sets
`window.valourDisableOffscreenCanvasWorkers`, and the loading scene in
`Valour.Client` then draws on the main thread instead of in a worker.

Photino identifies itself as `Photino WebView` by default. The host sets a
Safari-style user agent instead, because libraries such as the voice client
read the user agent to detect engine differences.

## Calls on Linux

Calls need a WebKitGTK that is compiled with WebRTC. The WebKitGTK packages in
Ubuntu, Fedora, and Arch leave it out, so with them `RTCPeerConnection` is
undefined and joining a call fails with "Failed to connect to voice".

Released WebKitGTK versions through 2.54 implement WebRTC on GStreamer, and that
implementation cannot publish media to LiveKit. It does not support the
`balanced` bundle policy, its DTLS handshake does not complete when it takes
the server role, and its outgoing packets lack the `sdes:mid` header extension
that the server uses to identify each track. The WebKit development branch
implements WebRTC with libwebrtc instead. With a build of it, Valour calls carry
audio in both directions through both voice providers, Cloudflare RealtimeKit
and LiveKit.

The Linux release is a Flatpak that bundles such a build. The WebKit commit
is pinned in [`Tools/Linux/WebKit/COMMIT`](../../Tools/Linux/WebKit/COMMIT),
and [`fetch-source.sh`](../../Tools/Linux/WebKit/fetch-source.sh) checks it out
and turns WebRTC on. Everything else about the build is in
[`Tools/Linux/Flatpak`](../../Tools/Linux/Flatpak).

## Packaging

Two workflows produce the Linux release, each for x86_64 and aarch64:

1. **Linux WebKit** (`.github/workflows/linux-webkit.yml`) compiles WebKitGTK
   against the GNOME 51 runtime with `webkit.yml` and publishes the result as
   a release named by `Tools/Linux/webkit-key.sh`. The name changes whenever
   the commit, the source preparation, or the build options change, and the
   workflow only runs then or on request. It compiles three files at a time,
   because the largest WebCore files need several gigabytes each with the SDK's compiler
   and hosted runners have 16 GB. A cold build can take longer than a hosted
   runner's six-hour limit. The build step stops in time to save the compiler
   cache, so rerunning the workflow continues from the compiled objects.
   Precompiled headers are turned off because they make compiles uncacheable.
   Each architecture's cache takes a few gigabytes of the repository's 10 GB
   Actions cache allowance.
2. **Linux Build** (`.github/workflows/linux.yml`) publishes the app, downloads
   the matching WebKit archive, and builds `Valour-linux-<arch>.flatpak` with
   `gg.valour.Valour.yml`. It attaches both bundles to the version's release,
   like the Windows and Android builds.

To build locally, run the scripts inside the image the workflows use, started
with `--privileged`:

```sh
docker run --rm --privileged -v "$PWD:/repo" -w /repo \
  ghcr.io/flathub-infra/flatpak-github-actions:gnome-51 \
  Tools/Linux/Flatpak/build-webkit.sh /repo/work /repo/dist
```

Then place `dist/webkit-<arch>.tar.zst` at `Tools/Linux/Flatpak/webkit.tar.zst`,
publish the app to `Tools/Linux/Flatpak/valour` with
`dotnet publish Valour/Client.Photino -c Release -r linux-<x64|arm64> --self-contained -o Tools/Linux/Flatpak/valour`,
and run `Tools/Linux/Flatpak/build-app.sh /repo/artifacts` the same way.
Install the bundle with `flatpak install Valour-linux-<arch>.flatpak`. When
Docker runs in a virtual machine that shares the checkout from the host, such
as Colima on macOS, add `-v flatpak-app-work:/repo/Tools/Linux/Flatpak/.build-app`
so the app build's work directory is on the machine's own disk; unpacking
WebKit onto the shared folder fails with permission errors.

Only Debug builds read `VALOUR_API_BASE`, so a bundle for testing against a
local server is built from a Debug publish and started with
`flatpak run --env=VALOUR_API_BASE=http://<server>:<port> gg.valour.Valour`.

Inside the Flatpak, WebKit runs from `/app` and preferences live under
`~/.var/app/gg.valour.Valour/config/valour`. The sandbox grants network, sound,
display, device (for cameras), Downloads folder, notification, and Secret
Service access. WebKit's own process sandbox is turned off in this build.

For quick tests outside Flatpak, [`Tools/Linux/WebKit`](../../Tools/Linux/WebKit)
builds the same WebKit on Ubuntu 24.04 and installs it under
`/opt/valour-webkit`:

```sh
docker build -t valour-webkit-builder Tools/Linux/WebKit
docker run --rm -e JOBS=5 -v valour-webkit-build:/build valour-webkit-builder
```

Copy `/build/stage/opt/valour-webkit` from the volume to `/opt/valour-webkit`
and start Valour with `LD_LIBRARY_PATH=/opt/valour-webkit/lib`. WebKit starts
its helper processes from that fixed path, so the directory cannot be moved.
Besides the packages listed under Running, the machine needs `libopus0`,
`libavif16`, and `libatomic1`, and a PulseAudio or PipeWire sound server for
call audio.

## Other limitations

There is no tray icon, single-instance handling, pop-out windows, or automatic
update support yet.
