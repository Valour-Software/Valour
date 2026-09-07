# Deployment

The repository includes two deployment configurations. The root Compose bundle
runs a self-contained instance with local data services and Caddy. The files in
this directory run an application behind nginx with external PostgreSQL and Redis.
Choose the configuration that matches the infrastructure you operate.

## Self-hosted instance

From the repository root, copy `.env.example` to `.env` and set `VALOUR_DOMAIN`,
`POSTGRES_PASSWORD`, `VALOUR_ADMIN_EMAIL`, and `VALOUR_ADMIN_PASSWORD`. Point the
domain to the host, allow inbound ports 80 and 443, then run:

```sh
docker compose up -d
```

The bundle uses `ghcr.io/valour-software/valour:main-latest`. PostgreSQL data lives
in `pgdata`, media in `media`, and Caddy certificates/configuration in its named
volumes. Redis runs as a separate service. The first-run bootstrap creates a staff
account only when one does not already exist.

Caddy obtains and renews HTTPS certificates, proxies WebSockets, and permits
request bodies up to 250 MB. All configured application hosts collapse to the
single `VALOUR_DOMAIN`. The server applies database migrations during startup.
Back up persistent data and private configuration before deploying a changed build.

## Configuration and storage

Server settings use section/property names such as `Database:Host`. Environment
variables use `Database__Host`. `Config/appsettings.helper.json` lists settings,
and `Config/Configs/` defines defaults and computed configuration properties.

Compose interpolates only variables referenced in its YAML. Arbitrary settings
in `.env` are not automatically passed to the server. Add extra server environment
entries through a Compose override when configuring services beyond the bundle's
listed variables.

Filesystem storage is the bundle default. To use S3-compatible storage, override
`CDN__StorageMode` with `s3` and supply the endpoint, region, credentials, and
bucket names defined by `CdnConfig`. The optional MinIO profile starts storage:

```sh
docker compose --profile minio up -d
```

It does not switch Valour's storage driver or create the application's bucket
configuration. Configure the S3 driver and buckets separately.

Voice, email, payments, push notifications, and other integrations depend on their
configuration. The instance manifest at `/.well-known/valour-instance` reports
capabilities for clients. For LiveKit, follow [Self-hosted voice](SelfHostVoice.md).
For community-node setup, follow the federation checklist below.

## Application nodes behind nginx

The configuration in this directory expects Docker Compose, an external Docker
network named `valour-network`, and external PostgreSQL and Redis. It starts an
nginx container and a Valour application container. A deployment briefly runs both
blue and green application containers while traffic changes destination.

```text
HTTPS -> nginx -> valour-blue:5000 or valour-green:5000
                       -> external PostgreSQL and Redis
```

The application mounts `dotnet/appsettings.json` and
`dotnet/firebase-credentials.json` read-only. The Compose file supplies the runtime
environment, listening URL, Firebase credential path, and IdGen worker ID.
nginx loads `nginx/upstream.conf` to select the active application and uses
`nginx/ssl/valour.crt` and `valour.key` for TLS. Adjust hostnames in `nginx.conf`
to match the deployment.

Copy this directory's deployment files into the chosen server directory. Provide
the private configuration and certificate files there. Update the paths in
`valour-autodeploy.service` to that directory. The deploy script requires executable
permission and the systemd timer runs it every two minutes.

Create the Docker network and an initial upstream file before starting nginx.
Bring up the initial application with explicit `VALOUR_IMAGE`, `VALOUR_COLOR`,
and `VALOUR_WORKERID` values using `docker-compose.app.yml`, then verify health
and start nginx with `docker-compose.nginx.yml`. Record the active color and digest
before enabling automated deployment so the script can identify the live instance.

## Blue/green deployment

`deploy-if-new.sh` holds a `flock` on `deploy.lock`, reads the remote image digest,
and compares it with `current.digest`. When the digest differs, it:

1. Starts the inactive color on `valour-network`, pinned to that digest.
2. Polls its `/healthz` endpoint up to 60 times, waiting two seconds between attempts.
3. Waits another eight seconds after readiness for service caches to warm.
4. Rewrites the nginx upstream, validates nginx configuration, and reloads nginx.
5. Records `active-color` and `current.digest` immediately after the switch.
6. Allows 15 seconds for requests and client reconnection before stopping the
   other color. A teardown failure is reported in the deploy log.

The supplied script assigns worker IDs 0 to blue and 1 to green. That allocation
covers one blue/green pair. If multiple machines write the same database, give
every concurrently running application instance a distinct worker ID, including
all overlapping deployment colors. Change the script's allocation per machine;
reusing its default pair across a shared-database cluster would create collisions.

Check deployment logs and lingering containers after a failure. Readiness and an
nginx reload do not by themselves verify every application feature or every
client's reconnection.

## Federation production checklist

Federation roles are explicit configuration:

| Role | Settings and shared resources |
| --- | --- |
| Hub cluster | `Federation:HubEnabled=true` on every replica; shared database, Redis, Data Protection KEK, and protocol version |
| Community node | `Federation:HubUrl` and `Federation:NodeDomain`; private database and key material; hub role disabled |
| Standalone | Neither federation role enabled |

For an enabled role, provide a secret base64-encoded 32-byte `DataProtection:Kek`
or a read-only `DataProtection:KekFile`. Keep it stable across restarts and outside
the repository and database. Public deployments require HTTPS and must leave
`Federation:AllowInsecure` disabled.

Hub replicas do not set community-node `HubUrl` or `NodeDomain`. Community nodes
allocate worker IDs within their own database deployment. Their domain and signing
key identify them to the hub; they do not need an official-cluster worker ID.

The exact protocol is defined by `ValourFederation.ProtocolVersion`. Descriptors,
user credentials, and server credentials must match it. Coordinate deployments
across participating servers, resolve pending migrations before changing the
protocol, and verify nodes and outstanding grants against the deployed version.

### Community-node setup wizard

Run this from the repository root:

```sh
./scripts/valour-node-setup
```

The wizard creates or updates `.env`, retains a timestamped private backup,
generates a KEK when needed, and sets file permissions to `0600`. It asks whether
the node accepts the registrant's planets and explicit hosting approvals, or any
eligible owner through public migration hosting.

Registration takes place in the hub while signed in as the operator:

1. Configure the node, make DNS and TLS reachable, and start Compose.
2. Check `https://your-node-domain/.well-known/valour-node`.
3. Open User Settings, Federation on the hub, register the bare domain, and copy
   the issued challenge.
4. Run the wizard again with that challenge and recreate the Valour container so
   it receives the changed environment.
5. Select Verify server on the hub. Re-register and verify after changing the
   public signing key or migration-hosting policy.

See [Federation](../Federation.md) for approvals, owner handoff steps, and transfer
limits. Test public joins, private invite redemption, forward migration, and
pull-back against isolated public HTTPS deployments before enabling production
traffic.

`FederationMultiOriginLiveTests` uses `LIVE_FEDERATION=1`, `LIVE_HUB`,
`LIVE_NODE_DOMAIN`, `LIVE_EMAIL`, `LIVE_PASSWORD`, and `LIVE_PLANET`. It needs two
origins and defaults to HTTPS. `LIVE_FEDERATION_INSECURE=1` is for development
only. Store test credentials privately.
