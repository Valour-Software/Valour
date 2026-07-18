namespace Valour.Shared.Models;

/// <summary>
/// Federation protocol constants shared by hubs, nodes, and clients.
/// </summary>
public static class ValourFederation
{
    public const int ProtocolVersion = 1;

    public const string HubWellKnownRoute = "/.well-known/valour-federation";
    public const string NodeWellKnownRoute = "/.well-known/valour-node";
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
    public int ProtocolVersion { get; set; } = ValourFederation.ProtocolVersion;

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
    public bool Discoverable { get; set; }
}

public class FederatedPlanetStubResponse
{
    public long Id { get; set; }
    public string NodeDomain { get; set; }
    public string Name { get; set; }
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

public class AcceptDomainRequest
{
    public string Domain { get; set; }
}
