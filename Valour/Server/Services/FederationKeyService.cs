using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using Valour.Config.Configs;

namespace Valour.Server.Services;

/// <summary>
/// Manages federation signing keys. ES256 (ECDsa P-256), private key
/// Data-Protection-encrypted in the DB. Two purposes:
/// - "hub": signs the tokens a hub mints for nodes; public half published as
///   JWKS at /.well-known/valour-federation.
/// - "node": signs a community node's own server-to-server request tokens;
///   public half published in the node's /.well-known/valour-node and stored
///   by the hub at verification.
/// </summary>
public class FederationKeyService
{
    private const string ProtectorPurpose = "Valour.Federation.SigningKey";

    public const string HubPurpose = "hub";
    public const string NodePurpose = "node";

    // Bounded caches so a rotated or revoked key takes effect within the TTL
    // instead of living forever. InvalidateCaches() forces it immediately.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, (SigningCredentials Creds, DateTime CachedAt)> CredentialCache = new();
    private static string _cachedJwks;
    private static DateTime _cachedJwksAt;

    /// <summary>Drops the signing-credential and JWKS caches so a rotation is picked up now.</summary>
    public static void InvalidateCaches()
    {
        CredentialCache.Clear();
        _cachedJwks = null;
    }

    private readonly ValourDb _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<FederationKeyService> _logger;

    public FederationKeyService(ValourDb db, IDataProtectionProvider dataProtection, ILogger<FederationKeyService> logger)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    /// <summary>
    /// Generates the signing keys this instance's roles need, on first run:
    /// a hub key when HubEnabled, a node key when NodeEnabled.
    /// </summary>
    public async Task EnsureKeysAsync()
    {
        if (FederationConfig.Current?.HubEnabled == true)
            await EnsureKeyAsync(HubPurpose);

        if (FederationConfig.Current?.NodeEnabled == true)
            await EnsureKeyAsync(NodePurpose);
    }

    private async Task EnsureKeyAsync(string purpose)
    {
        if (await _db.FederationKeys.AnyAsync(x => x.Active && x.Purpose == purpose))
            return;

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kid = Guid.NewGuid().ToString("N");
        var parameters = ecdsa.ExportParameters(false);

        var jwk = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64UrlEncoder.Encode(parameters.Q.X),
            ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y),
            ["kid"] = kid,
            ["alg"] = "ES256",
            ["use"] = "sig",
        });

        var privatePkcs8 = Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey());

        await _db.FederationKeys.AddAsync(new Valour.Database.FederationKey
        {
            Id = kid,
            Purpose = purpose,
            Algorithm = "ES256",
            PublicJwk = jwk,
            PrivateKeyProtected = _protector.Protect(privatePkcs8),
            Active = true,
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Generated {Purpose} federation signing key {Kid}", purpose, kid);
    }

    /// <summary>
    /// Signing credentials for the hub's token minting.
    /// </summary>
    public Task<SigningCredentials> GetHubSigningCredentialsAsync() => GetSigningCredentialsAsync(HubPurpose);

    /// <summary>
    /// Signing credentials for this node's server-to-server request tokens.
    /// </summary>
    public Task<SigningCredentials> GetNodeSigningCredentialsAsync() => GetSigningCredentialsAsync(NodePurpose);

    private async Task<SigningCredentials> GetSigningCredentialsAsync(string purpose)
    {
        if (CredentialCache.TryGetValue(purpose, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
            return cached.Creds;

        var key = await _db.FederationKeys
            .AsNoTracking()
            .Where(x => x.Active && x.Purpose == purpose)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (key is null)
            return null;

        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(_protector.Unprotect(key.PrivateKeyProtected)), out _);

        var credentials = new SigningCredentials(
            new ECDsaSecurityKey(ecdsa) { KeyId = key.Id },
            SecurityAlgorithms.EcdsaSha256);

        CredentialCache[purpose] = (credentials, DateTime.UtcNow);
        return credentials;
    }

    /// <summary>
    /// This node's public JWK, for publishing in /.well-known/valour-node.
    /// </summary>
    public async Task<string> GetNodePublicJwkAsync()
    {
        var key = await _db.FederationKeys
            .AsNoTracking()
            .Where(x => x.Active && x.Purpose == NodePurpose)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        return key?.PublicJwk;
    }

    /// <summary>
    /// The published JWKS document — hub keys only (node keys are private to
    /// their own S2S auth and never advertised by the hub).
    /// </summary>
    public async Task<string> GetJwksJsonAsync()
    {
        if (_cachedJwks is not null && DateTime.UtcNow - _cachedJwksAt < CacheTtl)
            return _cachedJwks;

        var keys = await _db.FederationKeys
            .AsNoTracking()
            .Where(x => x.Active && x.Purpose == HubPurpose)
            .Select(x => x.PublicJwk)
            .ToListAsync();

        var jwks = $"{{\"keys\":[{string.Join(",", keys)}]}}";
        _cachedJwks = jwks;
        _cachedJwksAt = DateTime.UtcNow;
        return jwks;
    }
}
