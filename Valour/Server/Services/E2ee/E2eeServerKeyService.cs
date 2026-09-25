using Microsoft.AspNetCore.DataProtection;
using Valour.Sdk.E2ee;

namespace Valour.Server.Services;

/// <summary>
/// Manages the Ed25519 key this server uses to attest to messages it seals to
/// channel keys: webhook messages, system messages, and history written before
/// a channel was encrypted. Clients verify the attestation when they open such
/// a message, and the server verifies it again when the message is reported.
/// </summary>
public class E2eeServerKeyService
{
    private const string ProtectorPurpose = "Valour.E2ee.ServerAttestationKey";
    private const string HeldKeyPurpose = "Valour.E2ee.HeldChannelKey";

    /// <summary>
    /// How long the published key list is reused before it is read again, so
    /// every instance sharing the database publishes the same keys.
    /// </summary>
    private static readonly TimeSpan PublicKeyRefresh = TimeSpan.FromMinutes(1);

    private static readonly SemaphoreSlim CreateLock = new(1, 1);
    private static (string KeyId, byte[] Seed)? _activeKey;
    private static volatile PublicKeyList _publicKeys;

    private sealed record PublicKeyList(Dictionary<string, byte[]> Keys, DateTime LoadedAt);

    private readonly ValourDb _db;
    private readonly IDataProtector _protector;
    private readonly IDataProtector _heldKeyProtector;

    public E2eeServerKeyService(ValourDb db, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _heldKeyProtector = dataProtection.CreateProtector(HeldKeyPurpose);
    }

    /// <summary>Protects a server-created channel key while no member holds it.</summary>
    public string ProtectHeldSecret(byte[] secret) => _heldKeyProtector.Protect(Convert.ToBase64String(secret));

    public byte[] UnprotectHeldSecret(string protectedSecret) =>
        Convert.FromBase64String(_heldKeyProtector.Unprotect(protectedSecret));

    /// <summary>
    /// Stores attestation public keys from the node a planet moved from, so
    /// messages it sealed still verify here.
    /// </summary>
    public async Task ImportPublicKeysAsync(IReadOnlyDictionary<string, byte[]> keys)
    {
        if (keys is null || keys.Count == 0)
            return;

        var ids = keys.Keys.ToList();
        var known = await _db.E2eeServerKeys.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        foreach (var (id, publicKey) in keys)
        {
            if (known.Contains(id) || string.IsNullOrEmpty(id) || id.Length > 64 || publicKey?.Length != E2eeCrypto.KeySize)
                continue;

            _db.E2eeServerKeys.Add(new Valour.Database.E2eeServerKey
            {
                Id = id,
                PublicKey = publicKey,
                Active = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        _publicKeys = null;
    }

    /// <summary>
    /// Signs data with the active key, creating one on first use.
    /// </summary>
    public async Task<(string KeyId, Func<byte[], byte[]> Sign)> GetSignerAsync()
    {
        var key = await GetActiveKeyAsync();
        return (key.KeyId, data => E2eeCrypto.Sign(key.Seed, data));
    }

    public async Task<Dictionary<string, byte[]>> GetPublicKeysAsync()
    {
        var cached = _publicKeys;
        if (cached is not null && DateTime.UtcNow - cached.LoadedAt < PublicKeyRefresh)
            return cached.Keys;

        var keys = await _db.E2eeServerKeys.AsNoTracking()
            .Select(x => new { x.Id, x.PublicKey })
            .ToDictionaryAsync(x => x.Id, x => x.PublicKey);
        _publicKeys = new PublicKeyList(keys, DateTime.UtcNow);
        return keys;
    }

    public async Task<byte[]> GetPublicKeyAsync(string keyId)
    {
        var keys = await GetPublicKeysAsync();
        if (keys.TryGetValue(keyId ?? string.Empty, out var key))
            return key;

        // Another node may have created a key since this node cached the list.
        // Unknown IDs reload the list at most every few seconds.
        if (_publicKeys is { } cached && DateTime.UtcNow - cached.LoadedAt < TimeSpan.FromSeconds(5))
            return null;
        _publicKeys = null;
        keys = await GetPublicKeysAsync();
        return keys.GetValueOrDefault(keyId ?? string.Empty);
    }

    private async Task<(string KeyId, byte[] Seed)> GetActiveKeyAsync()
    {
        if (_activeKey is not null)
            return _activeKey.Value;

        await CreateLock.WaitAsync();
        try
        {
            if (_activeKey is not null)
                return _activeKey.Value;

            var existing = await _db.E2eeServerKeys.AsNoTracking()
                .Where(x => x.Active && x.PrivateKeyProtected != null)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (existing is not null)
            {
                var seed = Convert.FromBase64String(_protector.Unprotect(existing.PrivateKeyProtected));
                _activeKey = (existing.Id, seed);
                return _activeKey.Value;
            }

            // Two instances sharing a database that start signing at the same
            // moment can each create a key here. Their key IDs come from
            // different public keys, so both rows are stored and stay active.
            // That is harmless: every attestation names the key that signed
            // it, both public keys are published, and an instance that starts
            // later uses the newest one.
            var (newSeed, publicKey) = E2eeCrypto.GenerateEd25519();
            var keyId = "srv-" + Base64Url.Encode(E2eeCrypto.Sha256(publicKey).AsSpan(0, 12));
            _db.E2eeServerKeys.Add(new Valour.Database.E2eeServerKey
            {
                Id = keyId,
                PublicKey = publicKey,
                PrivateKeyProtected = _protector.Protect(Convert.ToBase64String(newSeed)),
                Active = true,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            _activeKey = (keyId, newSeed);
            _publicKeys = null;
            return _activeKey.Value;
        }
        finally
        {
            CreateLock.Release();
        }
    }
}
