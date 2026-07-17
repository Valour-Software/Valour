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
