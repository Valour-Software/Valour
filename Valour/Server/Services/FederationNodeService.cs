using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Valour.Config.Configs;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Community-node-side federation: verifies hub-minted tokens against the
/// hub's published JWKS and exchanges them for node-local sessions backed by
/// shadow user rows. The user's real hub token never reaches this node, and a
/// compromised node holds nothing valid anywhere else.
/// </summary>
public class FederationNodeService
{
    private static readonly TimeSpan JwksCacheLifetime = TimeSpan.FromMinutes(10);
    // Offline invite redemption is an availability exception, not a second
    // long-lived revocation system. Never trust a hub key that has been stale
    // for more than this short, bounded outage window.
    private static readonly TimeSpan MaximumOfflineKeyAge = TimeSpan.FromMinutes(15);
    // Sessions are deliberately short. The SDK silently re-exchanges before
    // expiry, while an offline client must authenticate again instead of
    // retaining a stale federation grant indefinitely.
    // Keep the node-local bearer no longer than the hub credential it was
    // exchanged from. This bounds access after a hub-side membership revoke,
    // node suspension, or key change even if the community node is temporarily
    // unreachable for an explicit push revocation.
    private static readonly TimeSpan LocalSessionLifetime = TimeSpan.FromMinutes(15);

    private static readonly object JwksLock = new();
    private static JsonWebKeySet _cachedJwks;
    private static string _cachedHubIssuer;
    private static DateTime _jwksFetchedAt;

    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly PlanetMemberService _memberService;
    private readonly TokenService _tokenService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FederationNodeService> _logger;

    public FederationNodeService(
        ValourDb db,
        UserService userService,
        PlanetMemberService memberService,
        TokenService tokenService,
        IHttpClientFactory httpFactory,
        ILogger<FederationNodeService> logger)
    {
        _db = db;
        _userService = userService;
        _memberService = memberService;
        _tokenService = tokenService;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public static bool NodeEnabled => FederationConfig.Current?.NodeEnabled == true;

    private static readonly TimeSpan S2STokenLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Mints a short self-signed token (node key) authenticating this node's
    /// server-to-server requests to the hub. iss/sub = this node's domain,
    /// aud = the hub's issuer (its root domain).
    /// </summary>
    public async Task<string> MintS2STokenAsync(FederationKeyService keyService)
    {
        var credentials = await keyService.GetNodeSigningCredentialsAsync();
        if (credentials is null)
            return null;

        var hubIssuer = await GetHubIssuerAsync() ?? new Uri(FederationConfig.Current.HubUrl).Host;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = FederationConfig.Current.NodeDomain,
            Audience = hubIssuer,
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", FederationConfig.Current.NodeDomain),
            }),
            Claims = new Dictionary<string, object>
            {
                ["protocol"] = ValourFederation.ProtocolVersion,
            },
            Expires = DateTime.UtcNow.Add(S2STokenLifetime),
            IssuedAt = DateTime.UtcNow,
            SigningCredentials = credentials,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>
    /// Validates any hub-signed token against the hub's published JWKS (issuer,
    /// signature, lifetime, ES256). Audience/purpose are the caller's to check.
    /// Returns the claims, or null if invalid. Used for migration pull-back
    /// grants the hub signs and this node must honor.
    /// </summary>
    public async Task<IDictionary<string, object>> ValidateHubSignedTokenAsync(
        string token,
        string audience = null,
        bool allowStaleKeys = false)
    {
        if (!NodeEnabled || string.IsNullOrWhiteSpace(token))
            return null;

        var jwks = await GetHubJwksAsync(allowStaleKeys);
        if (jwks is null)
            return null;

        var expectedIssuer = await GetHubIssuerAsync(allowStaleKeys) ?? new Uri(FederationConfig.Current.HubUrl).Host;

        var validation = new TokenValidationParameters
        {
            ValidIssuer = expectedIssuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = audience,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
            RequireSignedTokens = true,
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, validation);
        // A valid signature alone is not a compatibility guarantee. Require
        // every federation credential to opt in to the currently supported
        // protocol rather than silently accepting a legacy token with a
        // compatible-looking audience or purpose.
        return result.IsValid && HasCurrentProtocol(result.Claims) ? result.Claims : null;
    }

    /// <summary>
    /// Materializes a membership authorized by an offline-verifiable invite and
    /// returns a short node-local session. The caller owns the surrounding
    /// database transaction so invite consumption and membership creation can
    /// commit or roll back together.
    /// </summary>
    public async Task<TaskResult<AuthToken>> ProvisionInviteSessionAsync(
        long hubUserId,
        string name,
        string subscription,
        long planetId,
        string issuedAddress)
    {
        var user = await EnsureShadowUserAsync(hubUserId, name, subscription);
        if (user is null)
            return TaskResult<AuthToken>.FromFailure("Failed to create local account.");

        var member = await _memberService.AddMemberAsync(planetId, hubUserId, doTransaction: false);
        if (!member.Success && !string.Equals(member.Message, "Already a member.", StringComparison.Ordinal))
            return TaskResult<AuthToken>.FromFailure(member.Message);

        return TaskResult<AuthToken>.FromData(await CreateLocalSessionAsync(hubUserId, issuedAddress));
    }

    private async Task<AuthToken> CreateLocalSessionAsync(long hubUserId, string issuedAddress)
    {
        // Multiple devices can legitimately hold sessions for the same hub
        // user. Keep them independent until their short expiry; membership
        // reconciliation revokes access separately for every device.
        await _db.AuthTokens
            .Where(x => x.AppId == "FEDERATION" && x.UserId == hubUserId && x.TimeExpires < DateTime.UtcNow)
            .ExecuteDeleteAsync();

        var token = new Valour.Database.AuthToken
        {
            AppId = "FEDERATION",
            Id = "val-" + Guid.NewGuid(),
            TimeCreated = DateTime.UtcNow,
            TimeExpires = DateTime.UtcNow.Add(LocalSessionLifetime),
            // A community node only needs the scopes used by the planet APIs.
            // FullControl would also unlock node-local OAuth, billing, account,
            // and administrative surfaces unrelated to a federated membership.
            Scope = Permission.CreateCode(
                UserPermissions.Minimum,
                UserPermissions.View,
                UserPermissions.Membership,
                UserPermissions.Invites,
                UserPermissions.PlanetManagement,
                UserPermissions.Messages),
            UserId = hubUserId,
            IssuedAddress = issuedAddress ?? "FEDERATION",
        };

        await _db.AuthTokens.AddAsync(token);
        await _db.SaveChangesAsync();
        return token.ToModel();
    }

    /// <summary>
    /// Exchanges a hub-minted federation token for a node-local auth token,
    /// creating or refreshing the shadow user row.
    /// </summary>
    public async Task<TaskResult<AuthToken>> ExchangeAsync(string hubToken, string issuedAddress)
    {
        if (!NodeEnabled)
            return TaskResult<AuthToken>.FromFailure("This instance is not a community node.");

        if (string.IsNullOrWhiteSpace(hubToken))
            return TaskResult<AuthToken>.FromFailure("Include the hub token.");

        var jwks = await GetHubJwksAsync();
        if (jwks is null)
            return TaskResult<AuthToken>.FromFailure("Could not load the hub's signing keys.");

        // The hub's canonical issuer is its root domain, which can differ from
        // the URL a node uses to reach it (reverse proxies, container
        // networking). Learn it from the hub's instance manifest; fall back to
        // the URL host if the manifest is unavailable.
        var expectedIssuer = await GetHubIssuerAsync() ?? new Uri(FederationConfig.Current.HubUrl).Host;

        var validation = new TokenValidationParameters
        {
            ValidIssuer = expectedIssuer,
            ValidAudience = FederationConfig.Current.NodeDomain,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            // Pin the signing algorithm. Hub keys are published as JWKS, so
            // without this an attacker could present a token in another
            // algorithm (e.g. an HMAC forged with the public key as secret)
            // and hope the validator confuses key types. ES256 only.
            ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
            RequireSignedTokens = true,
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(hubToken, validation);
        if (!result.IsValid)
        {
            _logger.LogInformation(result.Exception, "Federation token rejected");
            return TaskResult<AuthToken>.FromFailure("Invalid federation token.");
        }

        if (!HasCurrentProtocol(result.Claims))
            return TaskResult<AuthToken>.FromFailure("Unsupported federation protocol.");

        if (!result.Claims.TryGetValue("sub", out var subRaw) || !long.TryParse(subRaw?.ToString(), out var hubUserId))
            return TaskResult<AuthToken>.FromFailure("Token missing subject.");

        result.Claims.TryGetValue("name", out var nameRaw);
        result.Claims.TryGetValue("subscription", out var subscriptionRaw);
        var name = nameRaw?.ToString();
        var subscription = subscriptionRaw?.ToString();
        if (string.IsNullOrWhiteSpace(name))
            return TaskResult<AuthToken>.FromFailure("Token missing name.");

        var user = await EnsureShadowUserAsync(hubUserId, name, subscription);
        if (user is null)
            return TaskResult<AuthToken>.FromFailure("Failed to create local account.");

        // Materialize the hub-signed memberships as local PlanetMember rows so the
        // federated user is a real member — normal API/SignalR paths recognize them.
        // The hub is ground truth for who belongs where; the node only grants
        // membership the signed token vouches for, and only for planets it hosts.
        await MaterializeMembershipsAsync(result.ClaimsIdentity, hubUserId);

        return TaskResult<AuthToken>.FromData(await CreateLocalSessionAsync(hubUserId, issuedAddress));
    }

    /// <summary>
    /// Reconciles local PlanetMember rows with the hub-signed membership set.
    /// A federated shadow user has no independent authority on this node: an
    /// omitted membership is a revocation, not merely an absent addition.
    /// </summary>
    private async Task MaterializeMembershipsAsync(System.Security.Claims.ClaimsIdentity identity, long userId)
    {
        if (identity is null)
            return;

        var grantedPlanetIds = identity.FindAll("memberships")
            .Select(x => long.TryParse(x.Value, out var planetId) ? planetId : 0)
            .Where(x => x > 0)
            .ToHashSet();

        // Do not let a malicious/incorrect hub claim grant access to a planet
        // this node does not host. Restrict the set before adding *or* removing
        // rows so unrelated local data is never touched.
        var hostedPlanetIds = await _db.Planets
            .Where(x => !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync();
        grantedPlanetIds.IntersectWith(hostedPlanetIds);

        // Revoke stale local grants first. Calling the normal member service is
        // important: it evicts HostedPlanet permission caches and emits the
        // realtime delete, rather than leaving an in-memory authorization hole.
        var staleMemberIds = await _db.PlanetMembers
            .Where(x => x.UserId == userId && hostedPlanetIds.Contains(x.PlanetId) && !grantedPlanetIds.Contains(x.PlanetId))
            .Select(x => x.Id)
            .ToListAsync();

        foreach (var memberId in staleMemberIds)
        {
            // Revocation is an authorization correction, not a user-visible
            // mutation that must wait for the migration snapshot window.
            var removal = await _memberService.DeleteAsync(memberId, bypassMigrationLock: true);
            if (!removal.Success)
            {
                _logger.LogWarning(
                    "Failed to revoke stale federated membership {MemberId} for user {UserId}: {Message}",
                    memberId, userId, removal.Message);
            }
            else
            {
                await RevokeFederationSessionsAsync(userId);
            }
        }

        foreach (var planetId in grantedPlanetIds)
        {
            var add = await _memberService.AddMemberAsync(planetId, userId);
            if (!add.Success)
            {
                // "Already a member" is the common, benign case — keep it quiet.
                _logger.LogDebug(
                    "Federated membership for planet {PlanetId} user {UserId}: {Message}",
                    planetId, userId, add.Message);
            }
        }
    }

    /// <summary>
    /// Creates or refreshes the local shadow row for a hub user. Shadow users
    /// keep their hub user id (the id space is hub-global), carry no
    /// credentials, and refresh name/tier from token claims on each exchange.
    /// </summary>
    private async Task<Valour.Database.User> EnsureShadowUserAsync(long hubUserId, string name, string subscription)
    {
        var existing = await _db.Users.FindAsync(hubUserId);
        if (existing is not null)
        {
            // Only ever adopt a pre-existing FEDERATED shadow. If a real local
            // account happens to occupy this id (for example, due to imported
            // or independently seeded node-local data), refuse: adopting it would
            // hand the visitor a FullControl token for someone else's account.
            if (!existing.IsFederated)
            {
                _logger.LogError(
                    "Refusing federation exchange for hub user {UserId}: a non-federated local account already holds that id",
                    hubUserId);
                return null;
            }

            existing.Name = name;
            existing.SubscriptionType = string.IsNullOrWhiteSpace(subscription) ? null : subscription;
            existing.TimeLastActive = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return existing;
        }

        var user = new Valour.Database.User
        {
            Id = hubUserId,
            Name = name,
            // Local tag uniqueness beats mirroring the hub tag
            Tag = await _userService.GetUniqueTag(name),
            TimeJoined = DateTime.UtcNow,
            TimeLastActive = DateTime.UtcNow,
            Compliance = true,
            IsFederated = true,
            SubscriptionType = string.IsNullOrWhiteSpace(subscription) ? null : subscription,
            // Keep shadow records valid on installations whose legacy columns
            // remain NOT NULL at the database level.
            OldAvatarUrl = string.Empty,
            Status = string.Empty,
            PriorName = string.Empty,
            StarColor1 = string.Empty,
            StarColor2 = string.Empty,
        };

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            await _db.Users.AddAsync(user);
            // UserProfile's FK is not modeled as a navigation, so save the
            // principal first instead of relying on EF to infer insert order.
            await _db.SaveChangesAsync();
            await _db.UserProfiles.AddAsync(new Valour.Database.UserProfile
            {
                Id = hubUserId,
                Headline = "Federated account",
                Bio = $"{name} is visiting from the wider Valour network.",
                BorderColor = "#fff",
                GlowColor = string.Empty,
                TextColor = string.Empty,
                PrimaryColor = string.Empty,
                SecondaryColor = string.Empty,
                TertiaryColor = string.Empty,
                BackgroundImage = string.Empty,
            });
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create shadow user {UserId}", hubUserId);
            return null;
        }

        return user;
    }

    private async Task RevokeFederationSessionsAsync(long userId)
    {
        var tokenIds = await _db.AuthTokens
            .Where(x => x.AppId == "FEDERATION" && x.UserId == userId)
            .Select(x => x.Id)
            .ToListAsync();
        if (tokenIds.Count == 0)
            return;

        await _db.AuthTokens.Where(x => tokenIds.Contains(x.Id)).ExecuteDeleteAsync();
        foreach (var tokenId in tokenIds)
            _tokenService.RemoveFromQuickCache(tokenId);
    }

    /// <summary>
    /// The hub's canonical issuer (its root domain), read from the hub's
    /// instance manifest and cached alongside the JWKS.
    /// </summary>
    private async Task<string> GetHubIssuerAsync(bool allowStale = false)
    {
        lock (JwksLock)
        {
            if (_cachedHubIssuer is not null && DateTime.UtcNow - _jwksFetchedAt < JwksCacheLifetime)
                return _cachedHubIssuer;
        }

        try
        {
            var client = _httpFactory.CreateClient("federation");
            var url = FederationConfig.Current.HubUrl.TrimEnd('/') + "/.well-known/valour-instance";
            var manifest = await client.GetFromJsonAsync<InstanceManifest>(url);
            var issuer = manifest?.Hosts?.RootDomain;

            if (!string.IsNullOrWhiteSpace(issuer))
            {
                lock (JwksLock)
                {
                    _cachedHubIssuer = issuer;
                }
            }

            return issuer;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to fetch hub issuer from instance manifest");
            if (allowStale && HasUsableOfflineCache())
            {
                lock (JwksLock)
                    return _cachedHubIssuer;
            }
            return null;
        }
    }

    private async Task<JsonWebKeySet> GetHubJwksAsync(bool allowStale = false)
    {
        lock (JwksLock)
        {
            if (_cachedJwks is not null && DateTime.UtcNow - _jwksFetchedAt < JwksCacheLifetime)
                return _cachedJwks;
        }

        try
        {
            var client = _httpFactory.CreateClient("federation");
            var url = FederationConfig.Current.HubUrl.TrimEnd('/') + ValourFederation.HubWellKnownRoute;
            var json = await client.GetStringAsync(url);
            var jwks = new JsonWebKeySet(json);

            lock (JwksLock)
            {
                _cachedJwks = jwks;
                _jwksFetchedAt = DateTime.UtcNow;
            }

            return jwks;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to fetch hub JWKS");
            if (allowStale && HasUsableOfflineCache())
            {
                lock (JwksLock)
                    return _cachedJwks;
            }
            return null;
        }
    }

    private static bool HasUsableOfflineCache()
    {
        lock (JwksLock)
        {
            return _cachedJwks is not null && _cachedHubIssuer is not null &&
                   DateTime.UtcNow - _jwksFetchedAt <= MaximumOfflineKeyAge;
        }
    }

    private static bool HasCurrentProtocol(IDictionary<string, object> claims) =>
        claims is not null &&
        claims.TryGetValue("protocol", out var raw) &&
        int.TryParse(raw?.ToString(), out var protocol) &&
        protocol == ValourFederation.ProtocolVersion;
}
