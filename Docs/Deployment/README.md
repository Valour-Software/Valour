# Production node deployment reference

This documents how an official Valour node VM is deployed, based on the live
production setup. It is a reference for operators and the starting point for
the self-hosting work described in [../FederationPlan.md](../FederationPlan.md).
Nothing in this folder contains secrets — config and certificates are
bind-mounted from files that live only on the server.

## Topology

Each node is a single VM (Ubuntu 24.04, Docker + Compose v2) running two
containers on a shared Docker network:

```
                    ┌─────────────────────────────────────────┐
 Cloudflare ──443──▶│ nginx (TLS termination, websocket proxy)│
                    │   └── upstream.conf ─▶ valour-{color}   │
                    │ valour-node-blue   ──┐  (blue/green)    │
                    │ valour-node-green  ──┘  port 5000       │
                    └─────────────────────────────────────────┘
                         │                    │
                 Azure PostgreSQL        Azure Redis
                 (shared by all nodes)   (shared by all nodes)
```

- **Postgres and Redis are external** (managed services), shared by every node
  in the cluster. They are not part of the compose files.
- **App config** is bind-mounted read-only into the container:
  `dotnet/appsettings.json` and `dotnet/firebase-credentials.json`.
  The only environment variables set are `ASPNETCORE_ENVIRONMENT`,
  `ASPNETCORE_URLS`, and `Notifications__FirebaseCredentialPath` (env vars
  override `appsettings.json` via the standard .NET config layering).
- **nginx** terminates TLS (Cloudflare origin certificate at
  `nginx/ssl/valour.crt` / `valour.key`), serves all four subdomains
  (`app`, `api`, `cdn`, `threads`), and upgrades websockets for the SignalR
  hub at `/hubs/core`. `client_max_body_size` matches the 250 MB upload cap.

## Directory layout on the server

```
~/valour/
├── docker-compose.app.yml      # app service (blue/green instantiated per color)
├── docker-compose.nginx.yml    # nginx service
├── deploy-if-new.sh            # digest-watching blue/green deployer
├── active-color                # state: which color is live ("blue"/"green")
├── current.digest              # state: image digest currently deployed
├── dotnet/
│   ├── appsettings.json        # server config (SECRET — never commit)
│   └── firebase-credentials.json
└── nginx/
    ├── nginx.conf              # main server config
    ├── upstream.conf           # rewritten by deploy script on each cutover
    └── ssl/valour.crt|key      # origin certificate (SECRET — never commit)
```

## Blue/green deploy flow

`deploy-if-new.sh` runs every 2 minutes via a systemd timer
(`valour-autodeploy.timer` → `valour-autodeploy.service`, included here):

1. Compare the remote digest of `ghcr.io/valour-software/valour:main-latest`
   against `current.digest`; exit if unchanged. A `flock` on `deploy.lock`
   prevents overlapping runs.
2. Start the *other* color with `docker compose -f docker-compose.app.yml -p
   valour-{next}` on the shared external `valour-network`, pinned to the exact
   image digest.
3. Poll `http://valour-{next}:5000/healthz` (up to 2 minutes) until it returns
   `ready`.
4. Rewrite `nginx/upstream.conf` to point at the new color, `nginx -t`, then
   hot-reload nginx — zero-downtime cutover.
5. Tear down the old color and record the new `active-color` and
   `current.digest`.

First-time setup on a fresh VM:

```sh
docker network create valour-network
mkdir -p ~/valour/{dotnet,nginx/ssl}
# place appsettings.json, firebase-credentials.json, nginx.conf, certs
echo "upstream valour_backend { server valour-blue:5000; }" > ~/valour/nginx/upstream.conf
docker compose -f docker-compose.nginx.yml up -d
sudo cp valour-autodeploy.{service,timer} /etc/systemd/system/
sudo systemctl enable --now valour-autodeploy.timer
```

## Notes for self-hosters

This setup assumes the official cluster (external managed Postgres/Redis,
Cloudflare in front, config already written). The self-hosting milestone in
[../FederationPlan.md](../FederationPlan.md) turns this into a batteries-included
`docker compose up` that bundles Postgres and Redis and stores media on local
disk. That bundle will use **Caddy** (automatic HTTPS from a single
`VALOUR_DOMAIN` variable) instead of nginx — the nginx config here stays as
the production reference. Until it lands, use this folder as the
reverse-proxy/deploy reference.

## Federation production checklist

Federation is disabled by default. It has two roles:

- **Official hub cluster:** every official Valour application instance is a
  hub-capable replica. Set `Federation:HubEnabled=true` on **every** official
  node, not only a primary node. All replicas must share the official database,
  Redis deployment, stable Data Protection KEK, and release/protocol version;
  any replica may then serve node registration, grants, token exchange, and
  JWKS. Do not set `Federation:HubUrl` or `Federation:NodeDomain` on those
  nodes. `Node:WorkerId` must be distinct only among concurrent writers to the
  shared official database. It is not a federation identifier.
- **Community node:** set `Federation:HubUrl` and `Federation:NodeDomain`; do
  **not** enable `HubEnabled`. It does not register or consume an official
  `Node:WorkerId`: its local object ids are scoped to its domain, while the hub
  allocates global planet ids. `Node:WorkerId` is optional and matters only
  when multiple app instances write that node's own database; it can be reused
  by unrelated community nodes. Register and verify the node only after its
  public HTTPS endpoint serves `/.well-known/valour-node`. By default a node
  accepts only migrations of its registrant's own planets, plus explicit
  hosting approvals created by that registrant at the hub. A self-hosted node
  that deliberately wants to accept any eligible owner's planet can set
  `Federation:AllowPublicMigrations` to `true`; re-register and verify after
  changing that setting so the hub records the new policy.

For either role, provide a stable, secret, base64-encoded 32-byte
`DataProtection:Kek` (or a read-only `DataProtection:KekFile`) outside the
database and repository. Federation-enabled instances refuse to start without
it. Do not enable `Federation:AllowInsecure` on an internet-facing deployment.

Before the first cutover, apply the database migrations, verify the public
domain/DNS/TLS configuration, and test registration plus token exchange using
a non-production community node. The federation migrations add durable,
node-scoped account-deletion delivery and global planet-id allocation, so all app
instances must run the same release before enabling node traffic.

Federation protocol v5 is an exact-version protocol: node descriptors and all
federation credentials, including S2S credentials, must agree with the hub.
Deploy every official hub replica and every community node together, then
re-verify each node and reissue any still-open migration or invite grant. Do
not roll this version out while a migration is pending; abort it first or let
it complete and verify the destination.

### Community-node setup wizard

The repository includes a guided Docker Compose setup for operators who want to
run a community node:

```sh
./scripts/valour-node-setup
```

It creates or updates the deployment `.env`, keeps a timestamped private
backup, generates a stable 32-byte Data Protection KEK when needed, and asks
whether the node should be closed to third-party migrations (the default) or
open to every eligible owner. It writes only the settings it manages and marks
the `.env` mode `0600`; do not commit that file.

The wizard intentionally cannot register a node: that action must be made by
the operator while signed into the intended hub. The normal ceremony is:

1. Run the wizard, make DNS/TLS live, and start `docker compose up -d`.
2. Check `https://your-node-domain/.well-known/valour-node` is publicly
   reachable.
3. On the hub, open **User Settings → Federation**, register the bare node
   domain, and copy its generated challenge.
4. Run the wizard again, paste that challenge, and restart the Compose stack.
5. Click **Verify server** in the hub UI. Re-register and re-verify whenever
   the node public key or public-migration policy changes.

See [Federation](../Federation.md) for the complete role model, migration
ceremony, trust boundary, and current transfer limitations.

Before production enablement, complete an interactive test against isolated
public HTTPS hub and node deployments: accept the node warning, join a public
planet, redeem a recipient-bound private invite, perform a forward migration,
and pull it back. The SDK smoke test is opt-in:
`LIVE_FEDERATION=1`, `LIVE_HUB`, `LIVE_NODE_DOMAIN`, `LIVE_EMAIL`,
`LIVE_PASSWORD`, and `LIVE_PLANET` run
`FederationMultiOriginLiveTests`. It requires two real origins; localhost is
not an acceptable production test substitute. The test requires HTTPS unless
the explicitly development-only `LIVE_FEDERATION_INSECURE=1` is set.
