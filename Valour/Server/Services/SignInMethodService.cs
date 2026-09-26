using System.Security.Cryptography;
using Valour.Server.Email;
using Valour.Server.Users;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using CredentialType = Valour.Database.CredentialType;

namespace Valour.Server.Services;

/// <summary>
/// Manages the ways an account can sign in (rows in the credentials table):
/// listing and removing them, adding a password, device keys, and the short
/// identity proofs that sensitive changes require.
/// </summary>
public class SignInMethodService
{
    private const string ReauthKind = "reauth";
    private const string DeviceChallengeKind = "device-challenge";

    /// <summary>How long an identity proof stays valid. Any number of changes can use it.</summary>
    public static readonly TimeSpan ReauthLifetime = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan DeviceChallengeLifetime = TimeSpan.FromMinutes(2);

    private const int MaxDeviceKeysPerUser = 10;

    private readonly ValourDb _db;
    private readonly AuthTicketStore _tickets;
    private readonly UserService _userService;
    private readonly TokenService _tokenService;
    private readonly ILogger<SignInMethodService> _logger;

    public SignInMethodService(
        ValourDb db,
        AuthTicketStore tickets,
        UserService userService,
        TokenService tokenService,
        ILogger<SignInMethodService> logger)
    {
        _db = db;
        _tickets = tickets;
        _userService = userService;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// A proof belongs to the session that confirmed identity, so a proof
    /// that leaks can't be used from another session. <paramref name="MultiFactor"/>
    /// is true when an authenticator code was checked along the way.
    /// </summary>
    private record ReauthProof(long UserId, string SessionId, bool MultiFactor = false);

    private record DeviceChallenge(string KeyId, string Challenge);

    // Identity proofs //

    /// <summary>Creates an identity proof for the session making this request.</summary>
    public async Task<ReauthResponse> CreateReauthProofAsync(long userId)
    {
        var session = await _tokenService.GetCurrentTokenAsync();
        if (session is null || session.UserId != userId)
            throw new InvalidOperationException("Identity proofs are created for the signed-in session.");

        return await CreateReauthProofAsync(userId, session.Id, false);
    }

    /// <summary>
    /// Creates an identity proof for a session that was just created by
    /// signing in, which confirmed identity itself.
    /// </summary>
    public async Task<ReauthResponse> CreateReauthProofAsync(long userId, string sessionId, bool multiFactor)
    {
        var proof = await _tickets.CreateAsync(ReauthKind, new ReauthProof(userId, sessionId, multiFactor), ReauthLifetime);
        return new ReauthResponse { Proof = proof, ExpiresAt = DateTime.UtcNow.Add(ReauthLifetime) };
    }

    /// <summary>
    /// True when the proof is valid for this session and came from a sign-in
    /// that checked an authenticator code, so it can stand in for a new code.
    /// </summary>
    public async Task<bool> ProofIncludesMultiFactorAsync(long userId, string reauthProof)
    {
        if (string.IsNullOrWhiteSpace(reauthProof))
            return false;

        var proof = await _tickets.GetAsync<ReauthProof>(ReauthKind, reauthProof);
        if (proof is null || !proof.MultiFactor || proof.UserId != userId)
            return false;

        var session = await _tokenService.GetCurrentTokenAsync();
        return session?.Id == proof.SessionId;
    }

    /// <summary>
    /// Confirms that the account owner is present, by password or by a recent
    /// identity proof from the same session. Accounts without a password must
    /// use a proof.
    /// </summary>
    public async Task<TaskResult> ConfirmIdentityAsync(long userId, string password, string reauthProof)
    {
        if (!string.IsNullOrWhiteSpace(reauthProof))
        {
            var proof = await _tickets.GetAsync<ReauthProof>(ReauthKind, reauthProof);
            var session = proof is null ? null : await _tokenService.GetCurrentTokenAsync();
            if (proof is not null && proof.UserId == userId && session?.Id == proof.SessionId)
                return TaskResult.SuccessResult;

            return TaskResult.FromFailure("Your confirmation expired. Confirm it's you again.");
        }

        if (string.IsNullOrWhiteSpace(password))
            return TaskResult.FromFailure("Confirm it's you to make this change.");

        var credential = await GetPasswordCredentialAsync(userId);
        if (credential is null || string.IsNullOrWhiteSpace(credential.Identifier))
            return TaskResult.FromFailure("This account has no password. Confirm it's you with a linked account instead.");

        var result = await _userService.ValidateCredentialAsync(CredentialType.PASSWORD, credential.Identifier, password);
        if (!result.Success || result.Data?.Id != userId)
            return TaskResult.FromFailure(result.Message);

        return TaskResult.SuccessResult;
    }

    // Listing and removing //

    public Task<Valour.Database.Credential> GetPasswordCredentialAsync(long userId) =>
        _db.Credentials.FirstOrDefaultAsync(x => x.UserId == userId && x.CredentialType == CredentialType.PASSWORD);

    public Task<bool> HasCredentialAsync(long userId, long credentialId) =>
        _db.Credentials.AnyAsync(x => x.Id == credentialId && x.UserId == userId);

    public async Task<List<SignInMethodInfo>> GetMethodsAsync(long userId)
    {
        var credentials = await _db.Credentials
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        return credentials.Select(x => new SignInMethodInfo
        {
            Id = x.Id,
            Type = x.CredentialType,
            DisplayName = x.CredentialType == CredentialType.PASSWORD ? x.Identifier : x.DisplayName,
            CreatedAt = x.CreatedAt,
            LastUsedAt = x.LastUsedAt,
            DeviceKeyId = x.CredentialType == CredentialType.DEVICE_KEY ? x.Identifier : null,
        }).ToList();
    }

    /// <summary>
    /// Removes a sign-in method. The account must keep at least one method
    /// that works on a new device (a password or a linked account), so it can
    /// never be left impossible to sign in to.
    /// </summary>
    public async Task<TaskResult> RemoveMethodAsync(long userId, long credentialId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();

        // Lock the account so two removals at once can't each see the other's
        // method still present and together remove the last way in.
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE id = {userId} FOR UPDATE");

        var credentials = await _db.Credentials.Where(x => x.UserId == userId).ToListAsync();
        var target = credentials.FirstOrDefault(x => x.Id == credentialId);
        if (target is null)
            return TaskResult.FromFailure("Sign-in method not found.");

        if (CredentialType.CanRecoverAccount(target.CredentialType) &&
            !credentials.Any(x => x.Id != target.Id && CredentialType.CanRecoverAccount(x.CredentialType)))
        {
            return TaskResult.FromFailure(
                "This is your only way to sign in. Add a password or link another account before removing it.");
        }

        _db.Credentials.Remove(target);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Adds a password to an account that signs in only with linked accounts.
    /// The password credential is keyed by the account's email.
    /// </summary>
    public async Task<TaskResult> AddPasswordAsync(long userId, string newPassword)
    {
        var complexity = UserUtils.TestPasswordComplexity(newPassword);
        if (!complexity.Success)
            return TaskResult.FromFailure(complexity.Message);

        if (await GetPasswordCredentialAsync(userId) is not null)
            return TaskResult.FromFailure("This account already has a password. Change it in Security settings.");

        var privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == userId);
        if (privateInfo is null || string.IsNullOrWhiteSpace(privateInfo.Email))
            return TaskResult.FromFailure("This account has no email address.");

        _db.Credentials.Add(NewPasswordCredential(userId, privateInfo.Email, newPassword));
        await _db.SaveChangesAsync();
        await NotifyMethodAddedAsync(userId, "A password");
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Links a Google or Discord account. The unique index on (type,
    /// identifier) keeps one provider account from being linked twice, even
    /// when two links race.
    /// </summary>
    public async Task<TaskResult> LinkProviderAsync(long userId, string credentialType, string providerUserId, string label, string providerName)
    {
        var existing = await _db.Credentials.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CredentialType == credentialType && x.Identifier == providerUserId);
        if (existing is not null)
        {
            return existing.UserId == userId
                ? TaskResult.SuccessResult
                : TaskResult.FromFailure($"This {providerName} account is already linked to another Valour account.");
        }

        if (await _db.Credentials.AnyAsync(x => x.UserId == userId && x.CredentialType == credentialType))
            return TaskResult.FromFailure($"Another {providerName} account is already linked. Remove it first.");

        _db.Credentials.Add(new Valour.Database.Credential
        {
            Id = IdManager.Generate(),
            UserId = userId,
            CredentialType = credentialType,
            Identifier = providerUserId,
            DisplayName = label,
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Only the unique index means someone else has it. Other failures
            // must surface as errors instead of this misleading message.
            _db.ChangeTracker.Clear();
            return TaskResult.FromFailure($"This {providerName} account is already linked to another Valour account.");
        }

        await NotifyMethodAddedAsync(userId, $"Your {providerName} account {label}");
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Emails the account owner when a new way to sign in is added, so a
    /// method added by someone else doesn't go unnoticed. Delivery problems
    /// never block the change itself.
    /// </summary>
    private async Task NotifyMethodAddedAsync(long userId, string method)
    {
        if (!EmailManager.IsConfigured)
            return;

        try
        {
            var email = await _db.PrivateInfos.AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => x.Email)
                .FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(email))
                return;

            var text = $"{method} can now be used to sign in to your Valour account. " +
                       "If this wasn't you, reset your password right away and remove it under Settings, Connections.";

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await EmailManager.SendEmailAsync(email, "New sign-in method on your Valour account", text,
                $"<p>{System.Net.WebUtility.HtmlEncode(text)}</p>", timeout.Token);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not send the new sign-in method notice for user {UserId}", userId);
        }
    }

    public static Valour.Database.Credential NewPasswordCredential(long userId, string email, string password)
    {
        var salt = PasswordManager.GenerateSalt();
        return new Valour.Database.Credential
        {
            Id = IdManager.Generate(),
            UserId = userId,
            CredentialType = CredentialType.PASSWORD,
            Identifier = email,
            Salt = salt,
            Secret = PasswordManager.GetHashForPassword(password, salt),
            Iterations = PasswordManager.CurrentIterations,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public async Task MarkUsedAsync(long credentialId)
    {
        await _db.Credentials
            .Where(x => x.Id == credentialId)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.LastUsedAt, DateTime.UtcNow));
    }

    // Device keys //

    /// <summary>
    /// Registers a device's public key and returns the key ID the device keeps.
    /// The private key never leaves the device's hardware keystore.
    /// </summary>
    public async Task<TaskResult<string>> RegisterDeviceKeyAsync(long userId, string publicKeyBase64, string deviceName)
    {
        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(publicKeyBase64 ?? string.Empty);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            if (ecdsa.KeySize != 256)
                return TaskResult<string>.FromFailure("Device keys must be P-256 keys.");
        }
        catch (Exception)
        {
            return TaskResult<string>.FromFailure("The device key is not a valid public key.");
        }

        var count = await _db.Credentials.CountAsync(x => x.UserId == userId && x.CredentialType == CredentialType.DEVICE_KEY);
        if (count >= MaxDeviceKeysPerUser)
            return TaskResult<string>.FromFailure("Too many devices use fingerprint sign-in. Remove one in Settings, under Security, first.");

        var name = string.IsNullOrWhiteSpace(deviceName) ? "Device" : deviceName.Trim();
        if (name.Length > 64)
            name = name[..64];

        var keyId = AuthTicketStore.NewId();
        _db.Credentials.Add(new Valour.Database.Credential
        {
            Id = IdManager.Generate(),
            UserId = userId,
            CredentialType = CredentialType.DEVICE_KEY,
            Identifier = keyId,
            Secret = publicKey,
            DisplayName = name,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        await NotifyMethodAddedAsync(userId, $"Fingerprint sign-in on {name}");

        return TaskResult<string>.FromData(keyId);
    }

    public async Task<TaskResult<DeviceChallengeResponse>> CreateDeviceChallengeAsync(string keyId)
    {
        // Challenges are issued for unknown keys too, so this does not reveal
        // which key IDs exist. Verification fails for them later.
        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > 64)
            return TaskResult<DeviceChallengeResponse>.FromFailure("Missing device key.");

        var challenge = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var challengeId = await _tickets.CreateAsync(DeviceChallengeKind, new DeviceChallenge(keyId, challenge), DeviceChallengeLifetime);

        return TaskResult<DeviceChallengeResponse>.FromData(new DeviceChallengeResponse
        {
            ChallengeId = challengeId,
            Challenge = challenge,
        });
    }

    /// <summary>
    /// Checks a device's signature over a challenge. Each challenge works once.
    /// Returns the credential that signed.
    /// </summary>
    public async Task<TaskResult<Valour.Database.Credential>> VerifyDeviceSignatureAsync(
        string keyId, string challengeId, string signatureBase64)
    {
        const string failure = "Fingerprint sign-in failed. Sign in another way.";

        var challenge = await _tickets.TakeAsync<DeviceChallenge>(DeviceChallengeKind, challengeId);
        if (challenge is null || challenge.KeyId != keyId)
            return TaskResult<Valour.Database.Credential>.FromFailure(failure);

        var credential = await _db.Credentials.AsNoTracking().FirstOrDefaultAsync(x =>
            x.CredentialType == CredentialType.DEVICE_KEY && x.Identifier == keyId);
        if (credential?.Secret is null)
            return TaskResult<Valour.Database.Credential>.FromFailure(failure);

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(credential.Secret, out _);

            // Android's SHA256withECDSA produces DER-encoded signatures.
            var valid = ecdsa.VerifyData(
                Convert.FromBase64String(challenge.Challenge),
                Convert.FromBase64String(signatureBase64 ?? string.Empty),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);

            if (!valid)
                return TaskResult<Valour.Database.Credential>.FromFailure(failure);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Device key signature check failed for credential {CredentialId}", credential.Id);
            return TaskResult<Valour.Database.Credential>.FromFailure(failure);
        }

        return TaskResult<Valour.Database.Credential>.FromData(credential);
    }

    /// <summary>
    /// Removes every device key of an account, for example after a password
    /// reset, when the account may have been taken over.
    /// </summary>
    public Task RemoveDeviceKeysAsync(long userId) =>
        _db.Credentials
            .Where(x => x.UserId == userId && x.CredentialType == CredentialType.DEVICE_KEY)
            .ExecuteDeleteAsync();
}
