![Valour logo](Valour/Client/wwwroot/media/logo/wide/logo_wide_blue_black_trans.png)

# Valour

Valour is an open-source community platform built with .NET and Blazor.
Communities are called planets. Each planet can have chat channels, thread feeds,
wikis, voice and video calls, roles, an economy, and a village where members walk
around and meet. The client supports tabs, splits, and multiple open conversations.

Use the app at [app.valour.gg](https://app.valour.gg) or visit
[valour.gg](https://valour.gg) for the public website. The
[documentation index](Docs/README.md) covers development, hosting, and architecture.

## Codebase

| Directory | Purpose |
| --- | --- |
| `Config/` | Server configuration classes and a sample settings file |
| `Valour/Client/` | Shared Razor UI, client services, TypeScript, styles, and assets |
| `Valour/Client.Blazor/` | Browser WebAssembly host for the shared UI |
| `Valour/Client.Maui/` | Native application host |
| `Valour/Sdk/` | API client, models, caches, and SignalR connections |
| `Valour/Shared/` | Shared contracts, permissions, requests, and utilities |
| `Valour/Database/` | Entity Framework models and migrations |
| `Valour/Server/` | HTTP APIs, SignalR, application services, and content delivery |
| `Valour/Web/` | Public website and static exporter |
| `Valour/Tests/` | C#, JavaScript, and browser tests |
| `Tools/Villages/` | Artwork tools and isolated local test/server runners |

The web host references the shared client UI. The server serves API and SignalR
requests and can serve the browser client and its static assets. Bots use the same
SDK and API contracts as the client.

## Self-hosting

The root Docker Compose bundle includes Valour, PostgreSQL, Redis, Caddy, and
filesystem media storage. Point a public domain at the host and allow inbound
ports 80 and 443. From the repository root:

```sh
cp .env.example .env
```

Edit `.env` to set the domain, database password, and bootstrap administrator
credentials, then start the services:

```sh
docker compose up -d
```

Caddy obtains the HTTPS certificate and proxies the application. The Compose
file maps supported `.env` values into the container. Additional server settings,
such as S3 storage or optional integrations, need corresponding entries in the
service environment or a Compose override. Merely adding an arbitrary setting
to `.env` does not pass it to the application.

See [Deployment](Docs/Deployment/README.md) for storage, optional services, and
cluster operation. [Self-hosted voice](Docs/Deployment/SelfHostVoice.md) covers
the LiveKit overlay and required media ports.

## Federation

A federation hub owns accounts and the global planet registry. Registered
community nodes host planets on independent domains. Clients connect directly
to those nodes using destination-specific credentials issued by the hub.
Their normal Valour session token stays with the hub.

To configure a community node in the Compose bundle, run:

```sh
./scripts/valour-node-setup
```

The wizard configures the domain and hub, creates a private Data Protection key,
and asks which owners may move planets to the node. The operator then registers
and verifies the public node in the hub's User Settings, Federation screen.
Without federation settings, the Compose deployment runs as a standalone instance.

Planet owners start a move from their planet's Federation settings, import the
signed grant, verify the destination, and finalize source deletion. Read
[the federation guide](Docs/Federation.md) before moving a planet, including its
transfer limits and the distinction between a pending and completed handoff.

## Contribute

Install the at least the suggested SDK version in [global.json](global.json):
`11.0.100-rc.1`. Local server work also requires PostgreSQL and Redis.
JavaScript tests use Node.js, and browser tests require Playwright.
Native application builds require their platform workloads.

Restore dependencies from the repository root:

```sh
dotnet workload restore
dotnet restore
```

Create the ignored `Valour/Server/appsettings.json` using
[Config/appsettings.helper.json](Config/appsettings.helper.json) as a guide.
Configure `Database`, `Redis`, and `Node` for your local services. Choose filesystem
media storage for local uploads, and configure optional services only when needed.
The helper file is a list of settings with placeholders, not a ready-to-run config.

The server applies Entity Framework migrations on startup. Start it with:

```sh
dotnet run --project Valour/Server/Valour.Server.csproj
```

The development launch profile uses `https://localhost:5001` and
`http://localhost:5000`. The server's startup output identifies the listening URLs.
The browser client resolves its API address through its hosting configuration.

Build from the root with `dotnet build`. C# integration tests start application
services and need a dedicated test database and Redis instance. Use the
[isolated test runner](Valour/Tests/Browser/README.md#isolated-c-regression) rather
than running them with a personal or shared server configuration. JavaScript tests
run with `node --test Valour/Tests/Js/*.test.mjs` after compiling the client sources.

Release builds that reference the local village atlas require the matching private
art asset. Follow [Village tilesets](Docs/VillageTilesets.md) to generate or restore
it before publishing.

Read [Reactive models](Docs/ReactiveModelSystem.md) before changing model caches,
events, or node connections. For server work, see
[API routing](Valour/Docs/API_ROUTES.md) and [Roles](Valour/Docs/ROLES.md).
Use [GitHub issues](https://github.com/Valour-Software/Valour/issues) for bug reports
and feature discussions. Report vulnerabilities using [SECURITY.md](SECURITY.md).

## Trademark Notice

The name "Valour" is a trademark of Valour Software LLC, and a trademark application is pending. While the project is open-source, use of the trademark must not imply endorsement by Valour Software LLC or mislead others regarding the origin of the project. 

Forks or derivative projects may not use the name "Valour" or related branding without prior written permission from Valour Software LLC. Any use of the trademark outside the scope of this project requires explicit permission. 

While use of the trademark is not permitted for forks of the project itself, use of the mark is allowed for Valour bots, plugins, or integrations. If you are unsure, contact us!
