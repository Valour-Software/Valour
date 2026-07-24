using System.Security.Cryptography;
using Google.Authenticator;
using Microsoft.AspNetCore.DataProtection;
using Valour.Database;
using Valour.Server.Database;
using Valour.Shared;

namespace Valour.Server.Services;

public class MultiAuthService
{
    public const string ProtectorPurpose = "Valour.MultiAuth.Secret";

    /// <summary>
    /// Marks a stored secret as protected. Rows written before encryption
    /// existed have no prefix and are still plaintext.
    /// </summary>
    private const string ProtectedPrefix = "enc:";

    private readonly ValourDb _db;
    private readonly TwoFactorAuthenticator _tfa;
    private readonly IDataProtector _protector;
    private readonly ILogger<MultiAuthService> _logger;

    public MultiAuthService(ValourDb db, IDataProtectionProvider dataProtection, ILogger<MultiAuthService> logger)
    {
        _db = db;
        _tfa = new TwoFactorAuthenticator();
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    /// <summary>
    /// The TOTP shared secret is a credential: anyone who can read it can mint
    /// valid codes, so it is encrypted at rest like other sensitive material.
    /// </summary>
    private string Protect(string secret) =>
        ProtectedPrefix + _protector.Protect(secret);

    /// <summary>
    /// Reads a stored secret, transparently handling rows that predate
    /// encryption.
    /// </summary>
    private string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            return stored;

        try
        {
            return _protector.Unprotect(stored[ProtectedPrefix.Length..]);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to unprotect a stored MFA secret.");
            return null;
        }
    }

    public async Task<List<string>> GetAppMultiAuthTypes(long userId)
    {
        var multiAuths = await _db.MultiAuths.Where(x => x.UserId == userId && x.Type == "app" && x.Verified)
            .Select(x => x.Type)
            .ToListAsync();

        return multiAuths;
    }

    public async Task<TaskResult<CreateAppMultiAuthResponse>> CreateAppMultiAuth(long userId)
    {
        // Ensure the user doesn't already have an app multi auth
        var verifiedExisting = await _db.MultiAuths.FirstOrDefaultAsync(x => x.UserId == userId && x.Type == "app" && x.Verified);
        if (verifiedExisting != null)
            return TaskResult<CreateAppMultiAuthResponse>.FromFailure("User already has an app multi auth");

        // Generate a cryptographically secure random key
        byte[] keyBytes = new byte[20];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        var key = Convert.ToBase64String(keyBytes);

        var privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == userId);
        if (privateInfo == null)
            return TaskResult<CreateAppMultiAuthResponse>.FromFailure("User not found");

        var setupInfo = _tfa.GenerateSetupCode("Valour.gg", privateInfo.Email, key, false);

        // If there's an unverified one, update it with a new key
        var multiAuth = await _db.MultiAuths.FirstOrDefaultAsync(x => x.UserId == userId && x.Type == "app" && !x.Verified);
        if (multiAuth is not null)
        {
            multiAuth.Secret = Protect(key);
            multiAuth.CreatedAt = DateTime.UtcNow;
        } 
        else
        {
            multiAuth = new MultiAuth
            {
                Id = IdManager.Generate(),
                UserId = userId,
                Type = "app",
                Secret = Protect(key),
                CreatedAt = DateTime.UtcNow
            };

            await _db.MultiAuths.AddAsync(multiAuth);
        }

        await _db.SaveChangesAsync();

        return TaskResult<CreateAppMultiAuthResponse>.FromData(new CreateAppMultiAuthResponse
        {
            QRCode = setupInfo.QrCodeSetupImageUrl,
            Key = setupInfo.ManualEntryKey
        });
    }

    public async Task<TaskResult> VerifyAppMultiAuth(long userId, string code)
    {
        var multiAuth = await _db.MultiAuths.FirstOrDefaultAsync(x => x.UserId == userId && x.Type == "app");
        if (multiAuth == null)
            return TaskResult.FromFailure("Invalid");

        var secret = Unprotect(multiAuth.Secret);
        if (secret is null)
            return TaskResult.FromFailure("Invalid");

        bool result = _tfa.ValidateTwoFactorPIN(secret, code);

        if (result){
            var changed = false;

            // If the code is valid, set the verified flag to true
            if (!multiAuth.Verified){
                multiAuth.Verified = true;
                changed = true;
            }

            // Upgrade rows stored before secrets were encrypted.
            if (!multiAuth.Secret.StartsWith(ProtectedPrefix, StringComparison.Ordinal)){
                multiAuth.Secret = Protect(secret);
                changed = true;
            }

            if (changed)
                await _db.SaveChangesAsync();

            return TaskResult.SuccessResult;
        }

        return TaskResult.FromFailure("Invalid");
    }

    /// <summary>
    /// Verifies an already-enabled authenticator for a sensitive action. Unlike the
    /// setup verifier, this can never turn an unverified MFA record into a valid one.
    /// </summary>
    public async Task<TaskResult> VerifyEstablishedAppMultiAuth(long userId, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return TaskResult.FromFailure("Enter your authenticator code.");

        var multiAuth = await _db.MultiAuths.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Type == "app" && x.Verified);
        if (multiAuth is null)
            return TaskResult.FromFailure("You must enable MFA before transferring a planet.");

        var secret = Unprotect(multiAuth.Secret);
        if (secret is null)
            return TaskResult.FromFailure("That authenticator code is invalid.");

        return _tfa.ValidateTwoFactorPIN(secret, code.Trim())
            ? TaskResult.SuccessResult
            : TaskResult.FromFailure("That authenticator code is invalid.");
    }
    
    public async Task<TaskResult> RemoveAppMultiAuth(long userId)
    {
        var multiAuth = await _db.MultiAuths.FirstOrDefaultAsync(x => x.UserId == userId && x.Type == "app");
        if (multiAuth == null)
            return TaskResult.FromFailure("Invalid");

        _db.MultiAuths.Remove(multiAuth);
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }
}
