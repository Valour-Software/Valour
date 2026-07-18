using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Valour.Config.Configs;
using Valour.Database;
using Valour.Server.Cdn;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Hub-side federation: the community node registry (domain-challenge
/// verification) and minting of short-lived, audience-scoped tokens.
/// The hub is ground truth for accounts; nodes never see real Valour tokens.
/// </summary>
public class FederationHubService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    private readonly ValourDb _db;
    private readonly FederationKeyService _keyService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FederationHubService> _logger;

    public FederationHubService(
        ValourDb db,
        FederationKeyService keyService,
        IHttpClientFactory httpFactory,
        ILogger<FederationHubService> logger)
    {
        _db = db;
        _keyService = keyService;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public static bool HubEnabled => FederationConfig.Current?.HubEnabled == true;

    /// <summary>
    /// The issuer written into minted tokens: this hub's root domain.
    /// Nodes validate it against the host of their configured HubUrl.
    /// </summary>
    public static string Issuer => HostingConfig.Current.RootDomain;

    public async Task<TaskResult<FederatedNodeRegistrationResponse>> RegisterNodeAsync(long ownerId, string domain)
    {
        domain = NormalizeDomain(domain);
        if (domain is null)
            return TaskResult<FederatedNodeRegistrationResponse>.FromFailure("Invalid domain.");

        var existing = await _db.FederatedNodes.FindAsync(domain);
        if (existing is not null && existing.OwnerId != ownerId)
            return TaskResult<FederatedNodeRegistrationResponse>.FromFailure("Domain is already registered by another user.");

        if (existing is null)
        {
            existing = new FederatedNode
            {
                Domain = domain,
                OwnerId = ownerId,
                CreatedAt = DateTime.UtcNow,
            };
            await _db.FederatedNodes.AddAsync(existing);
        }

        // (Re)issue a challenge; verification resets on re-register
        existing.Status = FederatedNodeStatus.PendingVerification;
        existing.VerificationChallenge = Guid.NewGuid().ToString("N");
        existing.VerifiedAt = null;

        await _db.SaveChangesAsync();

        return TaskResult<FederatedNodeRegistrationResponse>.FromData(ToResponse(existing));
    }

    /// <summary>
    /// Fetches the node's /.well-known/valour-node document and activates the
    /// node when the served challenge matches.
    /// </summary>
    public async Task<TaskResult<FederatedNodeRegistrationResponse>> VerifyNodeAsync(long ownerId, string domain)
    {
        domain = NormalizeDomain(domain);
        var node = await _db.FederatedNodes.FindAsync(domain);
        if (node is null || node.OwnerId != ownerId)
            return TaskResult<FederatedNodeRegistrationResponse>.FromFailure("Node not found.");

        if (string.IsNullOrWhiteSpace(node.VerificationChallenge))
            return TaskResult<FederatedNodeRegistrationResponse>.FromFailure("No pending challenge. Re-register first.");

        var insecure = FederationConfig.Current?.AllowInsecure == true;
        var scheme = insecure ? "http" : "https";
        var url = $"{scheme}://{domain}{ValourFederation.NodeWellKnownRoute}";

        // The domain is attacker-influenced (any user can register one), and
        // we are about to make the hub fetch it. Apply the same SSRF guard as
        // all other outbound fetches: public IPs only, DNS-rebinding safe.
        // LAN/dev clone networks opt out via AllowInsecure.
        if (!insecure && !await OutboundUrlSafetyValidator.IsSafeAsync(url, _logger))
            return TaskResult<FederatedNodeRegistrationResponse>.FromFailure(
                "Node domain must resolve to a public address.");

        FederatedNodeWellKnown wellKnown;
        try
        {
            var client = _httpFactory.CreateClient("federation");
            var json = await client.GetStringAsync(url);
            wellKnown = JsonSerializer.Deserialize<FederatedNodeWellKnown>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "Node verification fetch failed for {Domain}", domain);
            return TaskResult<FederatedNodeRegistrationResponse>.FromFailure(
                $"Could not fetch {url}: {e.Message}");
        }

        if (wellKnown?.Challenge != node.VerificationChallenge)
            return TaskResult<FederatedNodeRegistrationResponse>.FromFailure(
                "The served challenge does not match. Set Federation:NodeChallenge on the node and restart it.");

        if (string.IsNullOrWhiteSpace(wellKnown.PublicJwk))
            return TaskResult<FederatedNodeRegistrationResponse>.FromFailure(
                "The node did not advertise a public key.");

        node.Status = FederatedNodeStatus.Active;
        node.VerificationChallenge = null;
        node.VerifiedAt = DateTime.UtcNow;
        node.LastSeenAt = DateTime.UtcNow;
        node.ReportedVersion = wellKnown.Version;
        node.NodePublicJwk = wellKnown.PublicJwk;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Federated node {Domain} verified and activated", domain);

        return TaskResult<FederatedNodeRegistrationResponse>.FromData(ToResponse(node));
    }

    /// <summary>
    /// Staff-only: suspend or reinstate a federated node (trust &amp; safety).
    /// A suspended node cannot mint tokens, pass S2S auth, or exchange, and its
    /// planets are hidden from discovery — but the row is kept for the record.
    /// </summary>
    public async Task<TaskResult> SetNodeSuspendedAsync(string domain, bool suspended)
    {
        var node = await _db.FederatedNodes.FindAsync(NormalizeDomain(domain));
        if (node is null)
            return TaskResult.FromFailure("Node not found.");

        if (suspended)
        {
            node.Status = FederatedNodeStatus.Suspended;
        }
        else
        {
            // Reinstate to Active if it was verified, else back to pending.
            node.Status = node.VerifiedAt is not null
                ? FederatedNodeStatus.Active
                : FederatedNodeStatus.PendingVerification;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Federated node {Domain} suspended={Suspended}", domain, suspended);
        return TaskResult.SuccessResult;
    }

    public async Task<FederatedNodeRegistrationResponse> GetNodeStatusAsync(long ownerId, string domain)
    {
        var node = await _db.FederatedNodes.FindAsync(NormalizeDomain(domain));
        if (node is null || node.OwnerId != ownerId)
            return null;

        return ToResponse(node);
    }

    /// <summary>
    /// Mints a short-lived token for the given user, valid only on the given
    /// node domain. Contains the identity and entitlement claims a node needs
    /// for feature parity.
    /// </summary>
    public async Task<TaskResult<FederationTokenResponse>> MintTokenAsync(Valour.Server.Models.User user, string domain)
    {
        domain = NormalizeDomain(domain);

        var node = await _db.FederatedNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Domain == domain);
        if (node is null || node.Status != FederatedNodeStatus.Active)
            return TaskResult<FederationTokenResponse>.FromFailure("Domain is not an active community node.");

        var credentials = await _keyService.GetHubSigningCredentialsAsync();
        if (credentials is null)
            return TaskResult<FederationTokenResponse>.FromFailure("Hub signing key unavailable.");

        var expires = DateTime.UtcNow.Add(TokenLifetime);
        var accountAgeDays = (int)(DateTime.UtcNow - user.TimeJoined).TotalDays;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = domain,
            Expires = expires,
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(),
                ["name"] = user.Name,
                ["tag"] = user.Tag,
                ["avatar_version"] = user.Version,
                ["subscription"] = user.SubscriptionType ?? "",
                ["account_age_days"] = accountAgeDays,
                ["protocol"] = ValourFederation.ProtocolVersion,
            },
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return TaskResult<FederationTokenResponse>.FromData(new FederationTokenResponse
        {
            Token = token,
            ExpiresAt = expires,
        });
    }

    /// <summary>
    /// Account-deletion tombstones since a given time, for nodes to honor.
    /// </summary>
    public async Task<List<long>> GetPurgedUserIdsSinceAsync(DateTime since)
    {
        return await _db.FederatedPurges.AsNoTracking()
            .Where(x => x.CreatedAt > since)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.SubjectUserId)
            .Take(1000)
            .ToListAsync();
    }

    /// <summary>
    /// Authenticates a node's server-to-server request from its self-signed
    /// bearer token. Returns the verified node domain, or null. The token's
    /// issuer names the claimed node; it is verified against that node's stored
    /// public key, with the hub's own issuer as the required audience.
    /// </summary>
    public async Task<string> AuthenticateNodeAsync(string bearer)
    {
        if (string.IsNullOrWhiteSpace(bearer))
            return null;

        JsonWebToken parsed;
        try
        {
            parsed = new JsonWebTokenHandler().ReadJsonWebToken(bearer);
        }
        catch
        {
            return null;
        }

        var claimedDomain = NormalizeDomain(parsed.Issuer);
        if (claimedDomain is null)
            return null;

        var node = await _db.FederatedNodes.AsNoTracking().FirstOrDefaultAsync(x => x.Domain == claimedDomain);
        if (node is null || node.Status != FederatedNodeStatus.Active || string.IsNullOrWhiteSpace(node.NodePublicJwk))
            return null;

        JsonWebKey signingKey;
        try
        {
            signingKey = new JsonWebKey(node.NodePublicJwk);
        }
        catch
        {
            return null;
        }

        var validation = new TokenValidationParameters
        {
            ValidIssuer = claimedDomain,
            ValidAudience = Issuer,
            IssuerSigningKey = signingKey,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
            RequireSignedTokens = true,
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(bearer, validation);
        if (!result.IsValid)
        {
            _logger.LogInformation(result.Exception, "Node S2S token rejected for {Domain}", claimedDomain);
            return null;
        }

        return claimedDomain;
    }

    private static FederatedNodeRegistrationResponse ToResponse(FederatedNode node) => new()
    {
        Domain = node.Domain,
        Status = node.Status.ToString(),
        Challenge = node.VerificationChallenge,
        VerifiedAt = node.VerifiedAt,
    };

    /// <summary>
    /// Lowercases and validates a node domain. A federated node must be a real
    /// registrable domain — IP literals are rejected outright (no stable
    /// identity, and they sidestep the point of tying nodes to domains). In
    /// AllowInsecure (dev/LAN) mode, single-label hosts like "localhost" are
    /// permitted; otherwise a dotted domain is required.
    /// </summary>
    public static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        domain = domain.Trim().ToLowerInvariant().TrimEnd('/');

        // Reject schemes/paths — this is a bare host[:port]
        if (domain.Contains('/') || domain.Contains(' '))
            return null;

        if (!Uri.TryCreate($"https://{domain}", UriKind.Absolute, out var uri))
            return null;

        // Domains only — never a bare IPv4/IPv6 address.
        if (uri.HostNameType != UriHostNameType.Dns)
            return null;

        // A real registrable domain has a dot. Single-label hosts (localhost,
        // internal hostnames) are only allowed in dev/LAN mode.
        var insecure = FederationConfig.Current?.AllowInsecure == true;
        if (!insecure && !uri.Host.Contains('.'))
            return null;

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }
}
