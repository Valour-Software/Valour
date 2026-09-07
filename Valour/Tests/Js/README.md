# Client JavaScript tests

These tests exercise the compiled client JavaScript with Node's built-in test
runner. Coverage includes village rendering helpers, terrain and wall resolution,
building input, tileset packing and selection, chat bubbles, spatial audio, and
LiveKit video-host lifecycle.

Compile the client sources before running the tests. The client project compiles
TypeScript to adjacent JavaScript files:

```sh
dotnet build Valour/Client/Valour.Client.csproj
node --test Valour/Tests/Js/*.test.mjs
```

The tests do not need an npm dependency installation. Use the file glob in the
command; passing the directory asks Node to resolve it as a module.

Each test supplies the browser API members it needs, such as `AudioContext`,
`Image`, or parts of `document`. These stubs check application logic without
starting a browser. They do not establish browser rendering or device behavior.

The LiveKit tests check sharing a remote track across inline and full-screen
hosts, removing hosts without stopping another view, and cleanup after a failed
SDK detach. The [browser suites](../Browser/README.md) exercise actual rendering,
input, and local SFU media paths.

`village-atlas-protection.test.mjs` checks the shared C#/JavaScript package vector,
corrupt and oversized payloads, shared blob URLs, recovery after failed downloads,
and unchanged loading for ordinary images. The browser library and atlas suites
check decoded pixels and the actual published files; the decoder unit tests do
not establish license compliance or resistance to determined extraction.

`ui-lifecycle.test.mjs` exercises teardown of animations, color pickers, file-drop
listeners, browser listeners, dock history ownership, and delayed input callbacks.
The standalone [browser lifecycle suite](../Browser/sentry-ui-lifecycle.mjs) checks
actual DOM input and listener teardown while recording console and network errors.
It serves local client modules and does not require a server or login:

```sh
node Valour/Tests/Browser/sentry-ui-lifecycle.mjs
```

Set `PLAYWRIGHT_MODULE` when Playwright is installed outside the repository, and
`BROWSER_EXECUTABLE` when using a separately installed Chromium browser.
