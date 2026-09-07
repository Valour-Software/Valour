# Federation architecture

Federation separates account identity from the server hosting a planet. The hub
owns accounts and the global planet registry. A community node stores its planets
in its own database and serves their HTTP and SignalR traffic directly to clients.
The hub issues credentials for a specific destination so the client can connect
without giving the community operator its normal Valour session token.

For registration and migration steps, see [Federation](Federation.md). This page
explains the services and boundaries behind those operations.

## Deployment roles

`Federation:HubEnabled` enables the hub role. Every application replica in a hub
cluster uses that role and shares the cluster database, Redis, and Data Protection
key-encryption key. `Federation:HubUrl` and `Federation:NodeDomain` enable the
community-node role. A standalone deployment can run with neither role enabled.

`Node:WorkerId` is an IdGen setting from 0 through 1023. Concurrent writers to one
database need distinct values, including containers overlapping during deployment.
Independent databases have independent worker-ID allocations. Federation identifies
community nodes by domain and their registered signing keys.

The root Compose bundle runs Valour, PostgreSQL, Redis, Caddy, and filesystem media
storage. Configuration classes live in `Config/Configs`. Environment variables use
double underscores between section and property names. See
[Deployment](Deployment/README.md) for the configuration and proxy details.

## Descriptors, registration, and keys

`/.well-known/valour-instance` describes instance capabilities and configured hosts.
`/.well-known/valour-node` publishes the community node's federation descriptor,
including its domain, protocol, verification challenge, public signing key, and
migration-hosting policy. They serve different purposes: a capability manifest
does not itself register or authorize a community node.

`FederationHubService` registers domains and verifies their live descriptors.
Verification checks the public destination, challenge, exact protocol version,
and P-256 signing key. The hub stores the verified key and rechecks the descriptor
before issuing user credentials. A node registrant can approve an owner or planet
for migration, or advertise public migration hosting.

`FederationKeyService` manages signing material. Private signing material stored
in the database is protected using ASP.NET Data Protection. Federation-enabled
servers require a stable, private `DataProtection:Kek` or `DataProtection:KekFile`
outside the database. Hub replicas share that material; independent community
nodes keep their own.

## Client authentication

A user accepts the community domain before connecting. The hub issues a signed
credential whose audience is that destination. `FederationNodeService` validates
its signature, issuer, audience, lifetime, algorithm, and protocol, then provisions
a local session. Hub credentials and node-local sessions have 15-minute lifetimes.
Node-to-hub server credentials have five-minute lifetimes and use the node's
registered signing key.

The node stores the user information and membership it needs to serve the planet.
The hub remains responsible for account state and user-wide features such as
friends and direct messages. Community responses cannot overwrite those hub
models in the SDK. External HTTP caches and model stores carry origin scope so
identical local IDs from different servers remain separate.

`ValourFederation.ProtocolVersion` in `Valour/Shared/Models/Federation.cs` defines
the wire version. Descriptors and credentials must match it exactly. A missing or
mismatched version is rejected.

## Planet registry and lifecycle

`FederationPlanetRegistryService` maintains the hub's registry of community-hosted
planets and allocates global planet IDs. Registry synchronization carries the
metadata needed for discovery and routing. The community node owns the local
planet data, while the hub records its destination.

Account deletion uses durable, node-scoped delivery through `FederationPurgeService`.
A community node processes the deletion records addressed to it. Outages therefore
do not require the hub and every community server to be online at the same moment
for deletion delivery to remain pending.

## Moving a planet

`FederationMigrationService` uses signed grants, database state, and import receipts
to coordinate a move. A forward grant belongs to one owner, source planet, and
destination. The destination presents the grant and its server identity to obtain
the snapshot. It records an import receipt so retrying a completed request does not
create another copy.

The source becomes read-only during the handoff. After import completion it stays
as a hidden recovery copy until the owner verifies the destination and explicitly
finalizes source deletion. A completed handoff cannot be cancelled into two writable
copies. Before completion, cancellation follows the migration service's checks and
can restore source writes.

Pull-back moves a community-hosted planet to the hub. The imported hub copy remains
hidden until the community node confirms its purge. A failed purge leaves resumable
state. To move between community nodes, pull back to the hub and then start a
forward migration to the next destination.

Snapshot data includes roles, permissions, memberships, moderation state, messages,
attachment metadata, tags, invites, read state, and boost history. Export rejects
encrypted node-local storage or voice credentials, custom planet or emoji assets,
and thread attachments that do not have a supported transfer path.

During pull-back, node-local CDN database pointers are removed because they cannot
refer to hub database rows. Attachment locations remain part of the imported data.
Imported history carries `ImportSource` identifying the community domain. That
records who supplied the history; the hub has not independently verified each
author claim made by the community server.

## Planet-owned storage and voice

A planet can configure S3-compatible storage and a LiveKit server independently of
whole-planet hosting. The server validates configuration and protects stored
credentials. The client receives a short upload grant and uploads directly to the
planet's bucket; readers fetch the resulting URL from that host.

Planet-hosted attachment metadata includes the reported hash and storage location.
The hash is supplied by the client, and the host controls the bytes and retention.
The platform's own CDN reference counting does not own those objects. Members must
understand which host receives their uploads and media requests.

`VoiceCoordinator` resolves an enabled planet voice configuration for planet calls,
otherwise using the instance provider. Private direct and group calls use the
instance provider. Media goes between clients and the selected voice server.
See [Self-hosted voice](Deployment/SelfHostVoice.md) and
[Direct and group calls](DirectAndGroupCalls.md).

## Offline private invitations

A recipient-bound grant and client-key-bound passport allow a limited offline join
when the hub is unreachable. The node verifies the grant, passport, and client
proof, records a receipt, and reconciles it with the hub after recovery. A reachable
hub's rejection is an error, not an offline fallback. The exact cache age,
revocation, and proof rules are in [Invite grants](FederationInviteGrants.md).

## Verification

Federation tests cover origin isolation, token validation, registry operations,
invites, and migration state. `FederationMultiOriginLiveTests` is opt-in and requires
separate hub and community origins. See the
[deployment checklist](Deployment/README.md#federation-production-checklist) for its
environment variables. A local service test does not establish that public DNS,
TLS, or cross-origin browser consent works in a deployed network.
