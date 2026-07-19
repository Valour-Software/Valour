# Offline federation invite grants

Federation protocol v5 lets a recipient join a private community-hosted planet
while the hub is unavailable. It intentionally does **not** let a community
node invent hub memberships or trust its ordinary local invite codes.

The official hub is a cluster role: every official app replica serves the same
hub APIs and reads the same shared signing key material. Community nodes trust
the official hub origin and protocol, not a particular official worker node.

## Capability chain

1. The hub planet owner creates a grant for one named hub account. The hub
   stores its random id, recipient, node, expiry, and revocation state, then
   returns a single-use hub-signed JWT capability to the owner.
2. A logged-in client obtains a seven-day hub-signed federation passport. The
   passport is bound to a P-256 public key generated on that client.
3. To redeem a grant, the client signs its `(grant id, destination domain,
   planet id, recipient id, max uses, passport id, user id)` tuple with the
   corresponding private key and sends the grant, passport, and signature
   directly to the destination node. Binding the destination and scope stops
   a modified copy from redirecting a usable proof to another server.
4. The node validates both hub signatures and verifies the client signature.
   When the hub is reachable, it first reports the exact proof and waits for
   the hub to accept the redemption before creating the local membership and
   short node-local session.
5. Only a genuine hub outage can use the bounded offline path: the node
   atomically records the redemption, creates the local shadow membership,
   and queues the passport and proof for reconciliation. The hub independently
   verifies the queued proof before adding the durable hub membership.

The recipient binding plus proof is important: a node that has observed a
passport cannot replay it to join that account to another private planet.

## Availability and limits

The node first refreshes its hub JWKS normally, but falls back to its last
cached keys only when the hub cannot be reached and both the issuer and JWKS
are no more than 15 minutes old. A freshly installed node without a cached
hub key cannot accept offline joins. A reachable hub's rejection fails closed;
the node never treats a revoked grant as an outage. Grants expire within 30
days and passports within seven days, limiting stale-key and revocation
windows.

Revocation and global use accounting are eventually consistent during an
outage. On recovery, the node retries queued receipts. A hub rejection
(revoked, expired, exhausted, invalid, or deleted recipient) removes the
node-local federated membership and revokes its federation session tokens.
Nodes retain the passport/proof only until the hub acknowledges or rejects the
receipt.

Every node well-known document and every federation credential (including
node-to-hub S2S credentials) carries the exact protocol version. A missing or
mismatched version is rejected. Deploy a protocol upgrade to the hub and every
node together; recipient-bound invite grants issued by an older protocol must
be revoked and reissued.

## Client storage

The SDK keeps the passport key pair in memory, and exposes explicit export and
import methods for clients that need offline joining after an app restart.
Production clients must persist that pair and its unexpired passport in
platform-secure storage. Never persist it in a plaintext preference, export,
log, analytics record, cloud backup, or send the private key to a hub or node.
