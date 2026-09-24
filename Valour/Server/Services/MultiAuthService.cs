using System.Security.Cryptography;
using System.Text;
using Google.Authenticator;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using Valour.Database;
using Valour.Server.Database;
using Valour.Server.Redis;
using Valour.Server.Utilities;
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

    /// <summary>
    /// TOTP time step in seconds, matching authenticator apps.
    /// </summary>
    private const int TotpStepSeconds = 30;

    /// <summary>
    /// Steps accepted on either side of the current one. One step allows about
    /// 30 seconds of clock drift or typing delay without widening the window
    /// in which a captured code stays usable.
    /// </summary>
    private const int TotpDriftSteps = 1;

    private readonly ValourDb _db;
    private readonly TwoFactorAuthenticator _tfa;
    private readonly IDataProtector _protector;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MultiAuthService> _logger;

    public MultiAuthService(
        ValourDb db,
        IDataProtectionProvider dataProtection,
        IConnectionMultiplexer redis,
        ILogger<MultiAuthService> logger)
    {
        _db = db;
        _tfa = new TwoFactorAuthenticator();
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Checks a TOTP code for the given user. Enforces a per-account failure
    /// limit and accepts each time step at most once, so a code seen over a
    /// shoulder or in a log cannot be replayed within its validity window.
    /// </summary>
    private async Task<TaskResult> CheckTotpAsync(long userId, string secret, string code, string invalidMessage)
    {
        var throttleKey = AuthAttemptThrottle.MultiFactorKey(userId);
        if (await AuthAttemptThrottle.IsLockedAsync(_redis, throttleKey, AuthAttemptThrottle.MultiFactorFailureLimit, _logger))
            return TaskResult.FromFailure(AuthAttemptThrottle.LockedMessage, StatusCodes.Status429TooManyRequests);

        var step = MatchTotpStep(secret, code?.Trim());
        if (step is null)
        {
            await AuthAttemptThrottle.RecordFailureAsync(_redis, throttleKey, _logger);
            return TaskResult.FromFailure(invalidMessage);
        }

        if (!await TryConsumeTotpStepAsync(userId, step.Value))
            return TaskResult.FromFailure("That code has already been used. Wait for a new code and try again.");

        await AuthAttemptThrottle.ResetAsync(_redis, throttleKey, _logger);
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Returns the time step the code belongs to, or null if it matches none
    /// of the steps inside the allowed drift.
    /// </summary>
    private long? MatchTotpStep(string secret, string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 6 || !code.All(char.IsAsciiDigit))
            return null;

        var codeBytes = Encoding.ASCII.GetBytes(code);
        var currentStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / TotpStepSeconds;

        for (var step = currentStep - TotpDriftSteps; step <= currentStep + TotpDriftSteps; step++)
        {
            var expected = Encoding.ASCII.GetBytes(_tfa.GeneratePINAtInterval(secret, step));
            if (CryptographicOperations.FixedTimeEquals(expected, codeBytes))
                return step;
        }

        return null;
    }

    /// <summary>
    /// Marks a time step as used for this user. Returns false if it was already
    /// used. The marker outlives the drift window, after which the step can no
    /// longer match anyway.
    /// </summary>
    private async Task<bool> TryConsumeTotpStepAsync(long userId, long step)
    {
        try
        {
            return await _redis.GetDatabase(RedisDbTypes.Cluster).StringSetAsync(
                $"auth:totp-used:{userId}:{step}", 1,
                TimeSpan.FromSeconds(TotpStepSeconds * (2 * TotpDriftSteps + 2)),
                When.NotExists);
        }
        catch (Exception e)
        {
            // Failing open keeps MFA usable during a Redis outage; the code
            // itself was still verified.
            _logger.LogWarning(e, "Failed to record TOTP use for user {UserId}", userId);
            return true;
        }
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

        var check = await CheckTotpAsync(userId, secret, code, "Invalid");
        if (!check.Success)
            return check;

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

        return await CheckTotpAsync(userId, secret, code, "That authenticator code is invalid.");
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
