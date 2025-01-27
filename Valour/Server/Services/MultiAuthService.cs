using System.Security.Cryptography;
using Google.Authenticator;
using Valour.Database;
using Valour.Server.Database;
using Valour.Shared;

namespace Valour.Server.Services;

public class MultiAuthService
{
    private readonly ValourDb _db;
    private readonly TwoFactorAuthenticator _tfa;

    public MultiAuthService(ValourDb db)
    {
        _db = db;
        _tfa = new TwoFactorAuthenticator();
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
        byte[] keyBytes = new byte[6];
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
            multiAuth.Secret = key;
            multiAuth.CreatedAt = DateTime.UtcNow;
        } 
        else
        {
            multiAuth = new MultiAuth
            {
                Id = IdManager.Generate(),
                UserId = userId,
                Type = "app",
                Secret = key,
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

        bool result = _tfa.ValidateTwoFactorPIN(multiAuth.Secret, code);

        if (result){
            // If the code is valid, set the verified flag to true
            if (!multiAuth.Verified){
                multiAuth.Verified = true;
                await _db.SaveChangesAsync();
            }

            return TaskResult.SuccessResult;
        }

        return TaskResult.FromFailure("Invalid");
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