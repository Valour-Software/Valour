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
    private static readonly TimeSpan LocalSessionLifetime = TimeSpan.FromHours(24);

    private static readonly object JwksLock = new();
    private static JsonWebKeySet _cachedJwks;
    private static string _cachedHubIssuer;
    private static DateTime _jwksFetchedAt;

    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly PlanetMemberService _memberService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FederationNodeService> _logger;

    public FederationNodeService(
        ValourDb db,
        UserService userService,
        PlanetMemberService memberService,
        IHttpClientFactory httpFactory,
        ILogger<FederationNodeService> logger)
    {
        _db = db;
        _userService = userService;
        _memberService = memberService;
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
    public async Task<IDictionary<string, object>> ValidateHubSignedTokenAsync(string token)
    {
        if (!NodeEnabled || string.IsNullOrWhiteSpace(token))
            return null;

        var jwks = await GetHubJwksAsync();
        if (jwks is null)
            return null;

        var expectedIssuer = await GetHubIssuerAsync() ?? new Uri(FederationConfig.Current.HubUrl).Host;

        var validation = new TokenValidationParameters
        {
            ValidIssuer = expectedIssuer,
            ValidateAudience = false,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
            RequireSignedTokens = true,
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, validation);
        return result.IsValid ? result.Claims : null;
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

        // Housekeeping: drop this user's expired federation sessions so they don't
        // accumulate unbounded across re-exchanges.
        await _db.AuthTokens
            .Where(x => x.AppId == "FEDERATION" && x.UserId == hubUserId && x.TimeExpires < DateTime.UtcNow)
            .ExecuteDeleteAsync();

        // Node-local opaque session, shorter than hub logins: tier changes and
        // revocations propagate on silent re-exchange.
        var token = new Valour.Database.AuthToken
        {
            AppId = "FEDERATION",
            Id = "val-" + Guid.NewGuid(),
            TimeCreated = DateTime.UtcNow,
            TimeExpires = DateTime.UtcNow.Add(LocalSessionLifetime),
            Scope = UserPermissions.FullControl.Value,
            UserId = hubUserId,
            IssuedAddress = issuedAddress ?? "FEDERATION",
        };

        await _db.AuthTokens.AddAsync(token);
        await _db.SaveChangesAsync();

        return TaskResult<AuthToken>.FromData(token.ToModel());
    }

    /// <summary>
    /// Creates local PlanetMember rows for the planets the hub-signed token
    /// vouches the user belongs to (and this node actually hosts). Idempotent and
    /// best-effort — re-run on every exchange, it heals missing memberships.
    /// </summary>
    private async Task MaterializeMembershipsAsync(System.Security.Claims.ClaimsIdentity identity, long userId)
    {
        if (identity is null)
            return;

        foreach (var claim in identity.FindAll("memberships"))
        {
            if (!long.TryParse(claim.Value, out var planetId))
                continue;

            // Only grant membership for planets that actually live on this node.
            if (!await _db.Planets.AnyAsync(x => x.Id == planetId && !x.IsDeleted))
                continue;

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
            // account happens to occupy this id (an id-space collision between
            // instances — see IdManager worker ids), refuse: adopting it would
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
        };

        try
        {
            await _db.Users.AddAsync(user);
            await _db.UserProfiles.AddAsync(new Valour.Database.UserProfile
            {
                Id = hubUserId,
                Headline = "Federated account",
                Bio = $"{name} is visiting from the wider Valour network.",
                BorderColor = "#fff",
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create shadow user {UserId}", hubUserId);
            return null;
        }

        return user;
    }

    /// <summary>
    /// The hub's canonical issuer (its root domain), read from the hub's
    /// instance manifest and cached alongside the JWKS.
    /// </summary>
    private async Task<string> GetHubIssuerAsync()
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
            return null;
        }
    }

    private async Task<JsonWebKeySet> GetHubJwksAsync()
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
            return null;
        }
    }
}
