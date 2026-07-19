namespace Valour.Shared.Models;

/// <summary>
/// Federation protocol constants shared by hubs, nodes, and clients.
/// </summary>
public static class ValourFederation
{
    // v5 makes community-node object identifiers local to their origin. Only
    // planet ids are allocated by the hub and globally routable; a client must
    // never treat arbitrary community-local ids as a shared Snowflake space.
    // Refuse older peers rather than allowing a mixed deployment to collide in
    // a client's cache.
    public const int ProtocolVersion = 5;

    public const string HubWellKnownRoute = "/.well-known/valour-federation";
    public const string NodeWellKnownRoute = "/.well-known/valour-node";

    // Audiences and purposes are deliberately distinct. A node must never
    // accept a generic hub token where it expects an invite or identity proof.
    public const string PassportAudience = "valour-federation-passport";
    public const string PassportPurpose = "federation-passport";
    public const string InvitePurpose = "federation-invite";
    public const string InviteProofPurpose = "federation-invite-redemption";

    /// <summary>
    /// Canonical payload a recipient signs before redeeming an offline invite.
    /// Bind the proof to every authorization-bearing grant field, rather than
    /// only its id: otherwise a modified copy of a real grant could trick a
    /// client into producing a proof that an attacker replays with the
    /// original grant at its legitimate destination.
    /// </summary>
    public static string BuildInviteProofPayload(
        string grantId,
        string nodeDomain,
        long planetId,
        long recipientUserId,
        int maxUses,
        string passportId,
        long userId) => string.Join('\n',
        $"{InviteProofPurpose}.v2",
        grantId,
        nodeDomain,
        planetId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        recipientUserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        maxUses.ToString(System.Globalization.CultureInfo.InvariantCulture),
        passportId,
        userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// Request to register a community node domain with the hub.
/// </summary>
public class FederatedNodeRegistrationRequest
{
    public string Domain { get; set; }
}

/// <summary>
/// Registration state returned by the hub. The node operator must make their
/// node serve the challenge at /.well-known/valour-node, then call verify.
/// </summary>
public class FederatedNodeRegistrationResponse
{
    public string Domain { get; set; }
    public string Status { get; set; }
    public string Challenge { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

/// <summary>
/// The document a community node serves at /.well-known/valour-node.
/// </summary>
public class FederatedNodeWellKnown
{
    public string Domain { get; set; }
    public string Challenge { get; set; }
    public string Version { get; set; }

    /// <summary>
    /// Explicit protocol advertised by the node. This is deliberately nullable:
    /// an older document that omits it must be rejected rather than silently
    /// treated as the current protocol version.
    /// </summary>
    public int? ProtocolVersion { get; set; }

    /// <summary>
    /// The node's public signing key (JWK JSON). The hub stores this at
    /// verification and uses it to authenticate the node's server-to-server
    /// requests.
    /// </summary>
    public string PublicJwk { get; set; }
}

/// <summary>
/// Request for a hub-minted federation token, valid only for one node domain.
/// </summary>
public class FederationTokenRequest
{
    public string Domain { get; set; }
}

public class FederationTokenResponse
{
    /// <summary>
    /// Short-lived signed JWT, audience-bound to the requested domain
    /// </summary>
    public string Token { get; set; }

    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// A client-held public signing key for a time-limited federation passport.
/// The matching private key signs each invite redemption, so a node cannot
/// replay the passport to redeem a different invite for the same account.
/// </summary>
public class FederationPassportRequest
{
    public string PublicJwk { get; set; }
}

public class FederationPassportResponse
{
    public string Token { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Opaque client-side cache material. The private key must only ever be stored
/// in platform-secure storage; it is never sent to a hub or community node.
/// </summary>
public class FederationPassportCache
{
    public string Passport { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string PrivateKeyPkcs8 { get; set; }

    /// <summary>
    /// Public hub signing keys cached with the passport so the client can
    /// verify an invite's destination before it sends its passport/proof to a
    /// community node during a short hub outage.
    /// </summary>
    public string HubJwks { get; set; }
    public DateTime HubJwksFetchedAt { get; set; }
}

/// <summary>
/// Request to exchange a hub-minted federation token for a node-local session.
/// </summary>
public class FederationExchangeRequest
{
    public string HubToken { get; set; }
}

/// <summary>
/// A node's upsert of one of its planet stubs at the hub. Id 0 on the reserve
/// call means "mint a new id".
/// </summary>
public class FederatedPlanetStubRequest
{
    public long Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>
    /// The hub account that owns the planet
    /// </summary>
    public long OwnerId { get; set; }

    public int MemberCount { get; set; }
    public bool Nsfw { get; set; }
    public bool Public { get; set; }
    public bool Discoverable { get; set; }
}

public class FederatedPlanetStubResponse
{
    public long Id { get; set; }
    public string NodeDomain { get; set; }
    public string Name { get; set; }
    public bool Public { get; set; }
    public bool Discoverable { get; set; }
}

/// <summary>
/// Owner request to migrate a planet to another host (a node domain, or the
/// hub root to migrate back to official).
/// </summary>
public class MigrationInitiateRequest
{
    public long PlanetId { get; set; }
    public string TargetDomain { get; set; }
}

/// <summary>
/// The signed grant authorizing the destination to pull the planet snapshot
/// from the source, plus where to pull it from.
/// </summary>
public class MigrationInitiateResponse
{
    public long PlanetId { get; set; }
    public string TargetDomain { get; set; }
    public string SourceDomain { get; set; }
    public string Grant { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Owner-visible migration state. It deliberately contains no grant or
/// snapshot material, so it is safe to refresh from the settings UI.
/// </summary>
public class MigrationStatusResponse
{
    public long PlanetId { get; set; }
    public string PlanetName { get; set; }
    public string TargetDomain { get; set; }
    public string Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Destination-side request: the owner hands the signed grant to their node,
/// which pulls the snapshot from the source and imports it.
/// </summary>
public class MigrationImportRequest
{
    public string Grant { get; set; }
    public string SourceUrl { get; set; }
}

/// <summary>
/// Where a community-hosted planet lives, returned by the hub when the client
/// resolves a stub planet's host.
/// </summary>
public class FederatedPlanetLocation
{
    public long PlanetId { get; set; }
    public string NodeDomain { get; set; }
    public string Name { get; set; }

    /// <summary>Whether the user has already accepted this node's domain.</summary>
    public bool DomainAccepted { get; set; }
}

/// <summary>
/// A user's membership in a community-hosted planet, from the hub's records.
/// </summary>
public class FederatedMembershipInfo
{
    public long PlanetId { get; set; }
    public string NodeDomain { get; set; }
    public DateTime JoinedAt { get; set; }
}

/// <summary>Hub-owner request to create a recipient-bound offline invite.</summary>
public class FederatedInviteGrantCreateRequest
{
    public long PlanetId { get; set; }
    public long IntendedUserId { get; set; }
    public int MaxUses { get; set; } = 1;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>The signed capability is returned only to the invite creator.</summary>
public class FederatedInviteGrantResponse
{
    public string Grant { get; set; }
    public string GrantId { get; set; }
    public long PlanetId { get; set; }
    public string NodeDomain { get; set; }
    public long IntendedUserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Node-side offline redemption. Passport is hub-verifiable; proof is an
/// ECDSA signature over this exact grant made by the key embedded in it.
/// </summary>
public class FederatedInviteRedeemRequest
{
    public string Grant { get; set; }
    public string Passport { get; set; }
    public string Proof { get; set; }
}

/// <summary>Node-to-hub delayed acknowledgement of an offline redemption.</summary>
public class FederatedInviteRedemptionReport
{
    public string GrantId { get; set; }
    public long UserId { get; set; }
    public long PlanetId { get; set; }
    public DateTime RedeemedAt { get; set; }
    public string Passport { get; set; }
    public string Proof { get; set; }
}

/// <summary>
/// A cursor-paginated, node-scoped account-deletion delivery page. The cursor
/// is a monotonic hub purge id, not a wall-clock value, so an offline node can
/// resume without a retention-window gap.
/// </summary>
public class FederatedPurgePage
{
    public List<long> UserIds { get; set; } = new();
    public long NextCursor { get; set; }
}

public class AcceptDomainRequest
{
    public string Domain { get; set; }
}
