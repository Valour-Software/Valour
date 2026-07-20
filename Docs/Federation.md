# Federation

Federation lets an official Valour planet move to an independently operated
community server while preserving the official network as the source of truth
for accounts, identity, and the global planet registry. A community server
hosts the planet's data and clients connect to it directly; the official hub
brokers only the narrow, destination-bound credentials required for that
connection.

```
                    Official Valour hub cluster
       every official app replica has HubEnabled=true
       accounts • node registry • planet registry • signed grants
                              |
            short-lived, destination-bound federation credentials
                              |
                              v
                    Community node (separate origin)
                       planet data • local sessions
```

The user's normal Valour session token never leaves the official hub.

Federation ships in three layers, numbered by how much of the platform moves
into your own hands:

1. **Layer 1 — your media and calls:** a planet on official infrastructure
   brings its own S3 bucket and/or LiveKit server (planet settings → Storage /
   Voice). Valour never touches those bytes.
2. **Layer 2 — community nodes:** run the server on your own domain and host
   whole planets inside the official network. This document covers that layer.
3. **Layer 3 — the whole platform:** run a standalone instance, or enable the
   hub role for an independent clone network. See
   [self-hosting in the README](../README.md#self-hosting) and the
   [federation plan](FederationPlan.md).

## Roles

Configuration names below use the normal `Section:Property` form. In an
environment variable, write the separator as `__`, for example
`Federation__HubEnabled=true`.

### Official hub cluster

Every official Valour application node is a hub-capable replica. This is not a
special primary server and it is not inferred from the node name or worker ID.
Each official instance must set:

```text
Federation:HubEnabled=true
```

All official instances must run the same federation protocol version and share
the official database, Redis deployment, and one stable `DataProtection:Kek`
(or the same `KekFile` material). That lets any replica serve the node
registry, federation APIs, JWKS document, migration grant minting, and token
exchange consistently. Do not configure `Federation:HubUrl` or
`Federation:NodeDomain` on official hub replicas.

`Node:WorkerId` only separates concurrent writers to the same database. It
must be distinct among those instances, but it is neither a federation identity
nor an allocation shared with community operators.

An isolated self-hosted network can also choose the hub role. It is a separate
network unless its clients and community nodes explicitly use that hub; it does
not become part of the official Valour network merely by enabling the setting.

### Community node

A community node is an independently operated server registered to a hub. It
sets:

```text
Federation:HubUrl=https://the-hub.example
Federation:NodeDomain=community.example
Federation:NodeChallenge=<issued during registration>
DataProtection:Kek=<stable, secret base64 32-byte value>
```

It must **not** set `Federation:HubEnabled=true`. Its `Node:WorkerId` is local
to its own deployment: a single instance can use `0`, and unrelated community
nodes may reuse a value. A community node normally has its own database and
does not share official infrastructure.

The node's hosting policy is closed by default:

- Its registrant may move their own official planets there.
- The registrant may approve another official owner, or one specific planet,
  through **User Settings → Federation** on the hub.
- Setting `Federation:AllowPublicMigrations=true` deliberately permits any
  eligible official owner to select the node. Re-register and verify after
  changing this policy, so the hub records it.

## Community-node operator flow

1. Run `./scripts/valour-node-setup` in the self-hosted Compose checkout, or
   configure the equivalent settings yourself. The wizard creates/updates the
   private `.env`, generates a KEK when needed, and asks about the hosting
   policy.
2. Make the bare public domain resolve to the server, open ports 80/443, start
   the stack, and confirm that
   `https://community.example/.well-known/valour-node` is publicly reachable.
3. Sign into the intended hub and open **User Settings → Federation**. Register
   the bare domain. The hub returns a challenge.
4. Put that challenge in `Federation:NodeChallenge` (re-run the wizard for the
   Compose deployment), restart the node, then click **Verify server** on the
   hub. The hub checks the public descriptor, protocol version, challenge, and
   node signing key before activating the node.
5. If the node is closed to public migrations, create any owner- or
   planet-specific hosting approvals on that same hub settings page.

Re-register and re-verify after changing the node's public signing key or
`AllowPublicMigrations` policy. A domain must remain publicly reachable: the
hub checks the live descriptor before issuing user credentials to that node.

## Planet-owner migration flow

Only the planet owner can start a forward migration, and only from a planet
currently hosted on the hub:

1. On official Valour, open the planet's settings → **Federation**. Enter the
   destination's bare domain and choose **Start migration**. There is currently
   a domain field, not a node directory/picker.
2. The hub requires an active, verified destination node with the matching
   protocol and an applicable hosting policy. The operation fails before
   locking the planet when the node has not approved the owner/planet.
3. The hub makes the source planet read-only and creates a short-lived,
   owner-bound grant for that exact destination.
4. Still signed into official Valour, the owner opens **User Settings →
   Federation**, pastes the grant, and imports it. The client derives the
   destination from the signed grant; the owner does not sign in directly to
   the community server.
5. Verify the imported planet on the destination. Only then use **User Settings
   → Federation** on the official hub to finalize deletion of the source copy.
   A pending migration may instead be cancelled to restore source writes.

There is no direct community-node-to-community-node migration. To change
community hosts, first complete a verified pull-back to the hub, then begin a
new forward migration.

Some data is deliberately not migrated until it has a safe, complete transfer
path. An export rejects planets with encrypted node-local storage or voice
credentials, thread attachments, or custom planet/emoji assets. Resolve or
remove those blockers before starting the handoff.

## Security and trust model

- A node proves control of its domain and publishes a P-256 public key through
  `/.well-known/valour-node`; the hub pins and continuously rechecks that key.
- Hub-minted credentials are signed, short-lived, and valid only for the
  destination domain. The destination exchanges them for its own local session.
- Joining a community node requires explicit domain acceptance. Community
  servers are independently operated and should be treated accordingly.
- The Data Protection KEK encrypts federation signing material stored in a
  database. Keep it out of source control and never reuse a development key in
  a shared or production deployment.
- Federation protocol versions must match exactly. Upgrade all official hub
  replicas and participating community nodes together; re-verify nodes and
  reissue open grants after a protocol upgrade.

## Local development

To test hub-side UI and migrations locally, configure the local server as a
hub with `Federation:HubEnabled=true` and a stable local-only
`DataProtection:Kek`, then restart the server and hard-refresh the client. The
Federation item appears only for hub-resident planets. It remains hidden while
editing a planet on an external community node because forward migrations are
hub-owned.

For an end-to-end test, run a second public HTTPS community node. Localhost is
not a valid production substitute because registration and normal federation
traffic require publicly reachable HTTPS origins.

## Related documentation

- [Deployment federation checklist](Deployment/README.md#federation-production-checklist)
- [Protocol and architecture plan](FederationPlan.md)
- [Offline recipient-bound invite grants](FederationInviteGrants.md)
