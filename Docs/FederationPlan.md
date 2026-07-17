# Federation Plan

Three layers of federation for Valour, from easiest-to-ship to most ambitious.
Each layer builds directly on the previous one. Guiding constraints:

- **Keep complexity as low as possible.**
- **Feature parity** between official and unofficial hosting wherever feasible.
- **Users always know when they are on unofficial infrastructure** — opt-in
  domain acceptance, warnings before joining or uploading, and a persistent
  icon by the planet name.
- **Liability separation**: for anything stored on third-party infrastructure,
  Valour must be able to say *"we never touched that file."* Valour does not
  receive, store, scan, or serve those bytes — control is genuinely handed off.

The one-sentence shape: make `Valour.Server` a self-contained, configurable
distribution — **one binary, three modes**: standalone instance (Layer 1),
registered community node inside the official network (Layer 3), and official
cluster node (today's deployment). Layer 2 (bring-your-own-S3) is a
planet-level feature on official infrastructure that shares Layer 3's
trust/warning model for media.

---

## Current-state findings that shape the design

From a full architecture exploration (July 2026):

- **"Nodes" today are replicas, not peers.** Every node shares one Postgres and
  one Redis; planet→node assignment is a Redis key (`planet:{id}`); routing
  happens via an `X-Server-Select` header consumed by infrastructure. The SDK
  already handles per-node HTTP clients, per-node SignalR connections, planet→
  node resolution, and 421-misdirect retries (`Valour/Sdk/Nodes/Node.cs`,
  `Valour/Server/Services/NodeLifecycleService.cs`). The client is already
  architected to talk to multiple servers — it just assumes one origin and one
  database behind them.
- **Auth is opaque and centralized.** Tokens are random `val-{guid}` strings
  validated by DB lookup (`Valour/Server/Services/TokenService.cs`), scoped by
  a `UserPermissions` bitmask. No JWT or asymmetric crypto exists anywhere;
  the closest primitive is an HMAC-signed unsubscribe token
  (`Valour/Server/Email/UnsubscribeTokenService.cs`). Cross-domain token
  minting is greenfield.
- **Media is one global R2 store.** Bucket names (`valourmps`,
  `valour-public`) are hardcoded in `Valour/Server/Cdn/CdnBucketService.cs`;
  files are content-addressed by SHA-256 with global dedup and
  reference-counted deletion. Attachment URLs are validated at message-send
  time against a host allowlist (`Valour/Server/Cdn/MediaUriHelper.cs`).
  A solid SSRF guard already exists for outbound fetches
  (`Valour/Server/Cdn/OutboundUrlSafetyValidator.cs`).
- **DMs, friends, and notifications are user-scoped and effectively
  centralized.** DMs are channels with `PlanetId = null` and no owning node.
  The "ground truth network" already owns exactly the right data for Layer 3.
- **Self-hosting today is contributor-mode only.** Hardcoded CORS origins and
  `valour.gg` literals in ~a dozen places, R2 assumed, no bundled compose for
  outsiders. A `HostingConfig` abstraction exists but is unfinished. The
  production deployment (per-node VM, blue/green, nginx) is documented in
  [Deployment/](Deployment/README.md).
- **Known bugs to fix first (Phase 0):**
  1. Cross-node user-event relay publishes to the wrong Redis channel:
     subscribers listen on `node-relay-{name}`
     (`NodeLifecycleService.cs:50`) but `RelayUserEventAsync` publishes to the
     bare node name (`NodeLifecycleService.cs:277`), so cross-node live pushes
     of DMs/notifications/friend events are silently dropped (DB writes still
     land).
  2. Presence broadcast has an acknowledged "will not work with node scaling"
     TODO in `CoreHubService` — status changes don't reach planets hosted on
     other nodes.

---

## Layer 1 — Painless self-hosting

**Goal:** `docker compose up` gives a working private instance: server,
Postgres, Redis, media on local disk, client served from the same container,
no cloud accounts required.

### 1a. Storage abstraction with a local-disk driver

Replace the static `CdnBucketService.Client`/`PublicClient` with an injected
`IObjectStorage` (`Put/Get/Delete/Exists/GetSignedUrl`) and two drivers:

- **S3-compatible** (current behavior — works with R2, MinIO, Garage, B2;
  bucket names become config).
- **FileSystem** — store under a configured path. Private files stream through
  the existing `/content/{category}/{userId}/{hash}` routes; public assets
  (avatars, icons, emoji) get a `/public-content/...` route with
  `ValourHosts.PublicCdnHost` pointing back at the server itself. The
  URL-builder helpers in `ISharedUser`/`ISharedPlanet` already read from
  `ValourHosts`, so this mostly falls out.

Considered and rejected: shipping MinIO in compose instead of a FileSystem
driver — another container and ops concept for people who want a small
instance. The FileSystem driver is small and is the right default; MinIO/
Garage remain the documented growth path via the S3 driver.

### 1b. Finish domain/config unification

Hunt down remaining literals so everything flows from `HostingConfig`:

- CORS allowlist (`Valour/Server/Program.cs` ~line 288)
- `MediaUriHelper` host allowlist
- `ProxyHandler` thread links and Twitch `parent=valour.gg`
- `ClientHosts`, `EmailManager` sender address
- Tenor API key hardcoded in `Valour/Sdk/Services/TenorService.cs`
- Sentry DSN in `Valour.Client.Blazor/Program.cs`

The client's runtime-config override (`valour-runtime-config.js`) is the right
pattern — keep it.

### 1c. Instance manifest endpoint

`GET /.well-known/valour-instance`: instance name, server version, federation
protocol version, and a **capability map** (voice, payments, email,
registration open, upload limits, hosts). The client reads it and adapts —
hide Stargazer UI when Stripe is unset, skip email verification when email is
off, hide voice when RealtimeKit is unconfigured. This formalizes the existing
ad-hoc degradation (the `ApiKey == "fake-value"` email bypass becomes
`Email.Enabled = false`) and **becomes the node descriptor for Layer 3**.

### 1d. Optional-service degradation

| Service | When unconfigured |
|---|---|
| Stripe | Subscription/VC purchase UI hidden; configurable default limits |
| SendGrid | Auto-verify accounts; password reset via admin CLI/panel |
| RealtimeKit | Voice/video disabled (self-hostable LiveKit driver is a later add) |
| PhotoDNA | Already optional |
| Firebase/VAPID | Push disabled; SignalR-only notifications |
| Sentry/Tenor | Config-driven, off by default |

**Postgres and Redis stay hard requirements.** Abstracting Redis away for
single-node mode would touch presence, relay, and planet assignment for
near-zero benefit — it's one tiny container in compose.

### 1e. Distribution polish

- `docker-compose.yml` for self-hosters (server + postgres + redis, optional
  `minio` profile), built from the production reference in
  [Deployment/](Deployment/README.md); env-var-first configuration (the
  `Section__Property` pattern already works — prod uses it for the Firebase
  path).
- The self-host bundle's reverse proxy is **Caddy, not nginx**: a ~10-line
  Caddyfile keyed on a single `{$VALOUR_DOMAIN}` env var gives automatic
  Let's Encrypt issuance and renewal, erasing the most painful self-hosting
  step (certificates). Production keeps nginx; the nginx config in
  [Deployment/](Deployment/README.md) remains the prod reference. The
  Caddyfile must mirror nginx's two behaviors that matter: websocket
  proxying for `/hubs/core` (automatic in Caddy) and a request body limit
  matching the 250 MB upload cap.
- First-run admin bootstrap (env credentials or a setup token printed to
  logs) replacing the hardcoded Victor seed as the only path.
- Serve the WASM client from the server by default (fallback already exists
  in `Program.cs`).
- Fix the README's stale .NET-10 instructions (repo is on .NET 11 preview).

**Effort: small-to-medium.** The storage abstraction and config hunt are the
bulk. No schema changes beyond capability flags.

---

## Layer 2 — Per-planet bring-your-own-S3 (hands-off model)

**Goal:** a planet's settings can point its media at the owner's S3-compatible
storage. Control genuinely transfers to the planet owner: **Valour never
receives, stores, scans, or serves those bytes.** This is a deliberate
liability boundary — for planet-hosted media, Valour's position is "we never
touched that file," the same posture it holds toward media on Layer 3
community nodes.

### Design

**Configuration (planet settings → "Bring your own storage"):**
- S3-compatible endpoint, bucket, region, access key/secret, and the public
  base URL media will be served from (the bucket's public URL or the owner's
  CDN domain in front of it).
- Credentials encrypted at rest (ASP.NET Data Protection; new small piece —
  no secrets-at-rest infra exists today).
- Endpoint validated with the existing `OutboundUrlSafetyValidator` rules at
  save time (HTTPS only, public IPs only — no pointing the platform's S3
  client at internal networks) plus a write/read/delete probe with a health
  indicator in settings. Storage health problems surface to planet admins
  only; broken media is the planet's responsibility.

**Upload path — bytes never touch Valour:**
1. Client asks the node for an upload grant for channel X.
2. Server checks the member's permissions and mints a **presigned S3 POST
   policy** against the planet's bucket (POST policy rather than presigned PUT
   so `content-length-range` can enforce size caps server-side; key prefix
   scoped per planet/user; short expiry).
3. Client uploads **directly to the owner's bucket**. Valour transfers zero
   bytes.
4. Client attaches the resulting public URL; the client also reports the
   file's SHA-256, stored on the attachment record for forensics/reporting
   (client-asserted, not verified — cheap and useful for abuse reports).

**Read path — direct from the owner's storage:**
- Attachment `Location` is the public URL on the planet's configured media
  host. No proxying, no signed-URL minting per view, normal browser/CDN
  caching.
- `MediaUriHelper.IsAllowedLocation` gains a per-planet rule: the planet's
  registered media host is an allowed attachment origin *for messages in that
  planet only*. Nothing changes for any other planet — that is the
  zero-risk-to-the-platform property.
- `MessageService.TryParseCdnBucketItemId` and the dedup/refcount machinery
  simply don't apply to planet-hosted attachments (no `cdn_bucket_items` row;
  a `storage: planet` marker on the attachment instead).

**Deliberate consequences of the hands-off model (documented, warned, accepted):**
- **No scanning.** PhotoDNA and any future scanning never see these files —
  that is the point. Moderation happens at the planet level: reports carry the
  URL + client-reported hash; Valour trust & safety can delist or suspend the
  planet, never the file.
- **Content mutability.** The owner can change bytes behind a URL after
  posting. Accepted; covered by the user warning.
- **Reader IP exposure.** Members fetching media connect to the owner's
  chosen host. Covered by the join warning.
- **EXIF stays unless the client strips it.** Today the server strips EXIF on
  upload; with direct upload that protection moves **client-side** (strip in
  the Blazor/MAUI client before the presigned POST). This is a required work
  item, not optional — uploader privacy parity.
- **Deletion is best-effort.** Valour deletes the attachment row; optionally
  (per-planet setting) issues an S3 delete with the stored credentials when a
  message is deleted. The owner ultimately controls retention.

**Warning & badge UX (consistent with Layer 3):**
- **Join warning** (first join of a planet with BYO media): media in this
  planet is hosted by the planet owner, not Valour; it is not scanned by
  Valour; your IP is visible to the owner's media host when viewing.
- **First-upload warning** (once per planet): "This file will be uploaded to
  storage controlled by the planet owner, not Valour. Valour never receives a
  copy and cannot recover or delete it for you."
- **Icon by the planet name** wherever the planet renders (sidebar, header,
  discovery) — same visual family as the Layer 3 community-node badge so
  "unofficial infrastructure" reads as one concept.

**Kept simple / out of scope:**
- Upload size caps default to the existing subscription tiers via the POST
  policy; per-planet overrides can come later (their storage, their cost).
- Planet icons/emoji stay on platform storage — tiny, needed for discovery UI
  even when the planet's storage is down, and they render outside the
  planet's warning context.
- **No custom endpoint for message/attachment *data*.** Storing message rows
  on a planet-supplied endpoint puts an untrusted dependency in the hottest
  path in the product (fetch, permissions, mentions, notifications) and its
  failure modes hit other users. Full data sovereignty is Layer 3's job.
  The zero-risk alternative that satisfies the underlying want: a **planet
  data mirror** — continuous async export of message/attachment metadata as
  JSON to the same planet bucket (write-only, out of every hot path).
  Combined with media already living there, an owner holds a complete
  restorable copy of their community, and it doubles as the migration path to
  a Layer 3 community node.

**Effort: medium.** Schema (`planet_storage_configs`, attachment storage
marker + reported hash), credential encryption, presigned POST minting,
per-planet allowlist rule, client-side EXIF stripping, settings UI + probes,
warnings/badge, optional mirror worker.

---

## Layer 3 — Community nodes: third-party planet hosting inside the network

**Goal:** anyone can run the (Layer 1) server on `planets.example.com`,
register it with the official network, and host planets that appear in the
ecosystem with feature parity — while valour.gg remains ground truth for
accounts, Stargazer status, friends, and DMs.

### Architecture choice

Rejected: **full peer federation** (ActivityPub/Matrix-style — identity would
federate too; enormous complexity and contradicts the ground-truth
requirement) and **hub-proxied remote nodes** (hub bears all load, double
latency, no real sovereignty). Chosen: **direct client↔node with hub-brokered
identity** — the client connects straight to the community node (HTTP +
SignalR), authenticated by short-lived tokens the hub mints per-domain. The
SDK's existing multi-node plumbing is most of the client work.

### Identity and token minting

The hub holds an **Ed25519 signing keypair**, public keys published at
`https://valour.gg/.well-known/valour-federation` (JWKS-style, key IDs for
rotation). Flow:

1. Client wants planet 123; hub's `api/node/planet/123` resolves to
   `external:planets.example.com`.
2. Client calls `POST hub/api/federation/token` for that domain. Hub returns a
   **short-lived (~15 min) signed JWT (EdDSA)**: `sub` (user id), `aud` (the
   node's domain — tokens are valid *only* for that domain, enforced by the
   audience claim), `exp`, plus parity claims: name/tag, avatar version,
   **subscription tier** (Stargazer perks and upload limits work remotely),
   email-verified flag, account age (anti-abuse signal for planet owners).
3. The node verifies the signature offline against cached hub keys, checks
   `aud` == its own domain, and **exchanges it for a node-local opaque
   `AuthToken`** — the existing `val-{guid}` mechanism. Past the exchange
   endpoint every existing server code path runs unmodified, which is where
   feature parity comes from. Node-local sessions silently re-exchange every
   ~24h so revocations and tier changes propagate.

The user's real Valour token never touches the community node. A compromised
node holds only audience-scoped, short-lived, unforgeable tokens — worthless
anywhere else.

On the node, first exchange creates a **shadow user row** (`IsFederated`, no
credentials, refreshed from claims each exchange) so FKs, permission caches,
and member machinery run untouched.

### Node registration and planet identity

- Operator generates a node keypair, registers the domain with the hub while
  signed in as the owning account (accountability), proves domain control via
  an ACME-style `/.well-known/valour-node` challenge, accepts the federation
  policy. Hub stores `federated_nodes` (domain, pubkey, owner, status,
  version, last-seen).
- **The hub mints planet IDs.** A community node requests an ID (signed
  request); the hub stores a **planet stub** (id, name, domain, owner, member
  count, NSFW/discovery flags). This keeps the global snowflake ID space
  intact — the SDK cache layer keys models by bare `long` IDs — and gives the
  hub the registry for discovery, invites, and moderation.

### Server-to-server surface (kept tiny)

Node→hub, all signed with the node key: **register/heartbeat** (with
version), **planet stub upsert**, **membership receipts** (join/leave), and
**notify** (offline push, below). Hub→node: **purge request** (account
deleted or domain access revoked — nodes are expected to honor it;
enforcement is reputational and the join warning says so plainly). No
node↔node traffic.

**Joins flow through the hub** (`POST hub/federation/join/{planetId}` → hub
records membership intent, returns a mint token). The hub has a record before
the node ever sees the user — nodes can't fabricate memberships to spam
people, and the hub can enumerate "your communities on other servers" for
client bootstrap and account deletion.

### DMs, notifications, presence

- **DMs never touch community nodes.** They're already hub-owned, user-scoped
  channels. Two people who meet on a remote planet DM through the hub. Zero
  new machinery, full parity, clean privacy story.
- **Live events** for a remote planet arrive on the client's existing SignalR
  connection to that node — same as internal nodes today. Unread state for
  remote planets lives on the node (it owns `UserChannelState` for its
  channels); the SDK already queries per-node.
- **Offline push** is the only gap: the node calls the hub's `notify`
  endpoint (signed, quota'd per node *and* per node-user pair, only valid for
  users with hub-recorded memberships); the hub writes the notification row
  and fires WebPush/FCM with its own credentials. Users can mute a domain
  entirely.
- Friends, presence, profiles: hub-owned, unchanged.

### Media on community nodes

The node stores its own media using its Layer 1 storage config (local disk or
its own S3). Same liability posture as Layer 2: Valour never touches those
bytes; the join warning covers storage and IP visibility; reports route to
hub T&S at planet/domain granularity.

### Safety and warning UX

- **First-contact interstitial, per domain:** "This planet is hosted by
  **planets.example.com**, operated by @owner — not by Valour. Messages and
  media you post are stored on their servers and visible to their operator.
  Valour moderation and data-deletion guarantees don't fully apply. Your IP
  address will be visible to this server." Accepting adds the domain to the
  user's **accepted-domains list stored at the hub** (syncs across devices).
- **Persistent badge/icon** on the planet name and header whenever inside a
  federated planet (shared visual language with the Layer 2 BYO-media icon).
- **Invites and discovery route through the hub**, so the warning renders
  *before* any connection to the third-party server. Discovery shows
  community-hosted planets only behind an "include community-hosted" toggle,
  badged.
- **Hub-signed domain denylist:** T&S can suspend a node (malware, CSAM,
  impersonation); clients refuse or hard-warn on listed domains.
- **Economy:** Valour Credits and Stripe never run on community nodes; planet-
  scoped currencies work locally; Stargazer perks arrive via token claims.
- **Voice:** node brings its own RealtimeKit config or voice is disabled —
  advertised via the instance manifest.

### Protocol versioning

`FederationProtocolVersion` constant in Shared, reported on heartbeat, hub
advertises a minimum, client warns on outdated nodes. The published
`Valour.Shared`/`Valour.Sdk` NuGets are the de-facto protocol SDK.

---

## Sequencing

| Phase | What | Size | Depends on |
|---|---|---|---|
| 0 | Fix relay-channel bug + cross-node presence TODO | S | — |
| 1 | Layer 1: storage abstraction, config unification, instance manifest, compose, bootstrap | M | — |
| 2 | Layer 2: BYO-S3 direct mode, credential encryption, client EXIF strip, warnings/badge, mirror | M | 1 (storage abstraction) |
| 3a | Signing infra, node registry + domain verification, token mint/exchange, shadow users | L | 1 |
| 3b | Client multi-origin nodes, join-via-hub, warning UX, accepted-domains list | M | 3a |
| 3c | Offline notify S2S, denylist, discovery integration, purge protocol | M | 3b |

Run 3a–3c behind a feature flag with one hand-picked partner node before
opening registration — the abuse surface (notify spam, impersonation planets,
malicious media hosts) is best tuned against a friendly node.

**Complexity-minimizing decisions, restated:** identity never federates (kills
the hardest problems in federation outright); DMs stay home; the token
exchange converts federated auth into the existing auth system at the node
boundary so the whole server codebase runs unmodified; Layer 2's per-planet
media host is scoped to that planet's messages only; and the S2S protocol is
five endpoints, not a protocol suite.

**Honest costs:** Layer 2's hands-off model trades scanning and content
immutability for the liability boundary — the warnings exist precisely
because those protections don't apply; Layer 3's weakest guarantee is
deletion on remote nodes (a purge *request*), and the join warning says so.
