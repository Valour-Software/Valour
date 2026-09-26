using System.Collections.Concurrent;
using System.Data;
using Valour.Database;
using Valour.Server.Cdn;
using Valour.Server.Cdn.Api;
using Valour.Server.Email;
using Valour.Server.Users;
using Valour.Server.Utilities;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Queries;
using Microsoft.EntityFrameworkCore.Storage;
using StackExchange.Redis;
using Valour.Shared.Models.Economy;
using AuthToken = Valour.Server.Models.AuthToken;
using EmailConfirmCode = Valour.Server.Models.EmailConfirmCode;
using GifFavorite = Valour.Server.Models.GifFavorite;
using PasswordRecovery = Valour.Server.Models.PasswordRecovery;
using Planet = Valour.Server.Models.Planet;
using TenorFavorite = Valour.Server.Models.TenorFavorite;
using User = Valour.Server.Models.User;
using UserChannelState = Valour.Server.Models.UserChannelState;
using UserPrivateInfo = Valour.Server.Models.UserPrivateInfo;
using UserProfile = Valour.Server.Models.UserProfile;

namespace Valour.Server.Services;

public class UserService
{
    /// <summary>
    /// Set on <see cref="TaskResult{T}.Code"/> by <see cref="ValidateCredentialAsync"/> when the
    /// credentials were correct but the account is disabled. Lets callers distinguish a disabled
    /// account from a bad credential without matching on the human-readable message.
    /// </summary>
    public const int AccountDisabledCode = 1001;

    /// <summary>
    /// Set on <see cref="TaskResult{T}.Code"/> by <see cref="ValidateCredentialAsync"/> when the
    /// account has had too many failed password attempts and checks are paused.
    /// </summary>
    public const int AccountThrottledCode = 1002;

    private const int EmailTimeoutSeconds = 20;
    private static readonly TimeSpan PasswordRecoveryCodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Referral rewards halve every this many rewarded referrals in 30 days.
    /// </summary>
    private const int ReferralRewardHalvingInterval = 5;

    /// <summary>
    /// Referrals past this many rewarded referrals in 30 days are recorded
    /// without a reward, which bounds what a farmed batch of accounts can earn.
    /// </summary>
    private const int MaxRewardedReferralsPerMonth = 25;

    /// <summary>
    /// Salt for the decoy hash computed when no credential matches, so an unknown
    /// account costs the same PBKDF2 work as a wrong password.
    /// </summary>
    private static readonly byte[] DecoySalt = PasswordManager.GenerateSalt();

    private readonly ValourDb _db;
    private readonly TokenService _tokenService;
    private readonly ILogger<UserService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly IConnectionMultiplexer _redis;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CdnBucketService _bucketService;


    /// <summary>
    /// The stored user for the current request
    /// </summary>
    private User _currentUser;

    public UserService(
        ValourDb db,
        TokenService tokenService,
        ILogger<UserService> logger,
        CoreHubService coreHub,
        NodeLifecycleService nodeLifecycleService,
        IConnectionMultiplexer redis,
        IHttpContextAccessor httpContextAccessor,
        CdnBucketService bucketService)
    {
        _db = db;
        _bucketService = bucketService;
        _httpContextAccessor = httpContextAccessor;
        _tokenService = tokenService;
        _logger = logger;
        _coreHub = coreHub;
        _nodeLifecycleService = nodeLifecycleService;
        _redis = redis;
    }

    public Task<int> GetUserCountAsync()
        => _db.Users.CountAsync();

    public async Task<IEnumerable<string>> GetNewUsersAsync(int count)
    {
        if (count < 1)
            return [];

        if (count > 50)
            count = 50;

        var users = await _db.Users.OrderByDescending(x => x.Id).Take(count).Select(x => x.Name).ToListAsync();
        return users;
    }
    
    /// <summary>
    /// Returns the user for the given id
    /// </summary>
    public async Task<User> GetAsync(long id) =>
        (await _db.Users.FindAsync(id)).ToModel();

    private readonly record struct UserAccessFlags(bool Disabled, bool ValourStaff, DateTime TimeCached);

    private static readonly ConcurrentDictionary<long, UserAccessFlags> AccessFlagsCache = new();
    private static readonly TimeSpan AccessFlagsTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Returns the disabled/staff flags for the given user, or null if the user does not exist.
    /// Checked on every authenticated request, so results are briefly cached in memory;
    /// the TTL also bounds staleness for flag changes made on other nodes.
    /// </summary>
    public async ValueTask<(bool Disabled, bool ValourStaff)?> GetAccessFlagsAsync(long userId)
    {
        if (AccessFlagsCache.TryGetValue(userId, out var cached) &&
            DateTime.UtcNow - cached.TimeCached < AccessFlagsTtl)
            return (cached.Disabled, cached.ValourStaff);

        var flags = await _db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.Disabled, x.ValourStaff })
            .FirstOrDefaultAsync();

        if (flags is null)
            return null;

        AccessFlagsCache[userId] = new UserAccessFlags(flags.Disabled, flags.ValourStaff, DateTime.UtcNow);
        return (flags.Disabled, flags.ValourStaff);
    }

    /// <summary>
    /// Evicts the cached access flags for a user. Call after changing Disabled or
    /// ValourStaff, or after deleting the user. Other nodes converge via the cache TTL.
    /// </summary>
    public static void InvalidateAccessFlags(long userId) =>
        AccessFlagsCache.Remove(userId, out _);

    public static void StartAccessFlagsSweepTask()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10));

                    var now = DateTime.UtcNow;
                    foreach (var entry in AccessFlagsCache)
                    {
                        if (now - entry.Value.TimeCached >= AccessFlagsTtl)
                            AccessFlagsCache.TryRemove(entry.Key, out _);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sweeping user access flags cache: {ex.Message}");
                }
            }
        });
    }

    /// <summary>Whether this id belongs to a hub-backed federation shadow user.</summary>
    public async Task<bool> IsFederatedAsync(long id) =>
        await _db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => x.IsFederated).FirstOrDefaultAsync();

    /// <summary>
    /// Queries users by the given attributes and returns the results
    /// </summary>
    public async Task<QueryResponse<User>> QueryUsersAsync(QueryRequest queryRequest)
    {
        var take = queryRequest.Take;
        if (take > 50)
            take = 50;
        
        var skip = queryRequest.Skip;

        var query = _db.Users
            .AsNoTracking();
            
        var search = queryRequest.Options?.Filters?.GetValueOrDefault("search");
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x =>
                EF.Functions.ILike((x.Name.ToLower() + "#" + x.Tag), "%" + search.ToLower() + "%"));

        query = query.OrderBy(x => x.Name);
        
        var totalCount = await query.CountAsync();

        var users = await query.Skip(skip)
            .Take(take)
            .Select(x => x.ToModel())
            .ToListAsync();

        return new QueryResponse<User>()
        {
            TotalCount = totalCount,
            Items = users
        };
    }

    public async Task<EmailConfirmCode> GetEmailConfirmCode(string code) =>
        (await _db.EmailConfirmCodes.FirstOrDefaultAsync(x => x.Code == code && x.ExpiresAt > DateTime.UtcNow)).ToModel();

    public async Task<UserProfile> GetUserProfileAsync(long userId) =>
        (await _db.UserProfiles.FirstOrDefaultAsync(x => x.Id == userId)).ToModel();

    public async Task<TaskResult<UserProfile>> UpdateUserProfileAsync(UserProfile updated)
    {
        var old = await _db.UserProfiles.FindAsync(updated.Id);
        if (old is null)
            return new TaskResult<UserProfile>(false, "Profile not found");
        
        // Color validation
        var colorsValid = 
            ColorHelpers.ValidateColorCode(updated.BorderColor) && 
            ColorHelpers.ValidateColorCode(updated.GlowColor) &&
            ColorHelpers.ValidateColorCode(updated.PrimaryColor) &&
            ColorHelpers.ValidateColorCode(updated.SecondaryColor) &&
            ColorHelpers.ValidateColorCode(updated.TertiaryColor) &&
            ColorHelpers.ValidateColorCode(updated.TextColor);
        
        if (!colorsValid)
            return new TaskResult<UserProfile>(false, "Invalid color code. Must be Hex and start with #.");
        
        // Headline validation
        if (updated.Headline is not null)
        {
            if (updated.Headline.Length > 40)
                return new TaskResult<UserProfile>(false, "Headline must be less than 40 characters.");
        }
        
        // Bio validation
        if (updated.Bio is not null)
        {
            if (updated.Bio.Length > 500)
                return new TaskResult<UserProfile>(false, "Bio must be less than 500 characters.");
        }
        
        // Bg image validation
        if (updated.BackgroundImage is not null && old.BackgroundImage != updated.BackgroundImage)
        {
            return new TaskResult<UserProfile>(false, "Background images must be updated via the content api.");
        }

        try
        {
            _db.Entry(old).CurrentValues.SetValues(updated);
            _db.UserProfiles.Update(old);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error updating user profile");
            return new TaskResult<UserProfile>(false, "Error updating user profile");
        }
        
        return new TaskResult<UserProfile>(true, "Profile updated", updated);
    }
    
    public async Task<List<Planet>> GetJoinedPlanetInfo(long userId)
    {
        var planetEntities = await _db.PlanetMembers
            // A completed federation handoff keeps a locked official recovery
            // copy until the owner finalizes deletion. Do not render that copy
            // alongside the community-hosted planet; the corresponding
            // FederatedMembership is loaded by the client instead.
            .Where(x => x.UserId == userId &&
                        !_db.FederatedMigrations.Any(m =>
                            m.PlanetId == x.PlanetId &&
                            m.Status == FederatedMigrationStatus.Completed))
            .Include(x => x.Planet)
            .ThenInclude(p => p.Tags) 
            .Select(x => x.Planet)
            .AsNoTracking()
            .ToListAsync();
    
        var planets = planetEntities
            .Select(p => p.ToModel())
            .ToList();
    
        foreach (var planet in planets)
        {
            planet.NodeName = await _nodeLifecycleService.GetActiveNodeForPlanetAsync(planet.Id);
        }
    
        return planets;
    }

    public async Task<PasswordRecovery> GetPasswordRecoveryAsync(string code) =>
        (await _db.PasswordRecoveries.FirstOrDefaultAsync(x => x.Code == code && x.ExpiresAt > DateTime.UtcNow)).ToModel();

    public async Task<Valour.Database.Credential> GetCredentialAsync(long userId) =>
        await _db.Credentials.FirstOrDefaultAsync(x => x.UserId == userId);

    public async Task<List<UserChannelState>> GetUserChannelStatesAsync(long userId) =>
        await _db.UserChannelStates.Where(x => x.UserId == userId).Select(x => x.ToModel()).ToListAsync();

    public async Task<List<TenorFavorite>> GetTenorFavoritesAsync(long userId) =>
        await _db.TenorFavorites.Where(x => x.UserId == userId).Select(x => x.ToModel()).ToListAsync();

    public async Task<List<GifFavorite>> GetGifFavoritesAsync(long userId) =>
        await _db.GifFavorites.Where(x => x.UserId == userId).Select(x => x.ToModel()).ToListAsync();

    public async Task<(List<User> outgoing, List<User> incoming)> GetFriendsDataAsync(long userId)
    {
        // Outgoing: Users this user has sent a friend request to (user -> other)
        var outgoing = await _db.UserFriends
            .Include(x => x.Friend)
            .Where(x => x.UserId == userId)
            .Select(x => x.Friend.ToModel())
            .ToListAsync();

        // Incoming: Users who have sent a friend request to this user (other -> user)
        var incoming = await _db.UserFriends
            .Include(x => x.User)
            .Where(x => x.FriendId == userId)
            .Select(x => x.User.ToModel())
            .ToListAsync();

        return (outgoing, incoming);
    }

    public async Task<List<User>> GetFriends(long userId)
    {
        // Users added by this user as a friend (user -> other)
        var added = _db.UserFriends.Where(x => x.UserId == userId);

        // Users who added this user as a friend (other -> user)
        var addedBy = _db.UserFriends.Where(x => x.FriendId == userId);

        // Mutual friendships
        var mutual = added.Select(x => x.FriendId).Intersect(addedBy.Select(x => x.UserId));

        var friends = await _db.Users.Where(x => mutual.Contains(x.Id)).Select(x => x.ToModel()).ToListAsync();

        return friends;
    }

    public async Task<UserPrivateInfo> GetUserPrivateInfoAsync(string email, bool makelowercase = true)
    {
        email = email?.Trim();
        if (string.IsNullOrEmpty(email))
            return null;

        if (!makelowercase)
            return (await _db.PrivateInfos.FindAsync(email)).ToModel();
        else
            return (await _db.PrivateInfos.FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower())).ToModel();
    }

    public async Task<TaskResult> SendPasswordResetEmail(UserPrivateInfo userPrivateInfo, string email, HttpContext ctx)
    {
        try
        {
            var oldRecoveries = _db.PasswordRecoveries.Where(x => x.UserId == userPrivateInfo.UserId);
            if (oldRecoveries.Any())
            {
                _db.PasswordRecoveries.RemoveRange(oldRecoveries);
                await _db.SaveChangesAsync();
            }

            string recoveryCode = Guid.NewGuid().ToString();

            var createdAt = DateTime.UtcNow;
            PasswordRecovery recovery = new()
            {
                Code = recoveryCode,
                UserId = userPrivateInfo.UserId,
                CreatedAt = createdAt,
                ExpiresAt = createdAt.Add(PasswordRecoveryCodeLifetime)
            };

            await _db.PasswordRecoveries.AddAsync(recovery.ToDatabase());
            await _db.SaveChangesAsync();

            // Never build this from the request's Host header: an attacker could
            // request a reset for a victim with a forged Host and receive the code.
            string link = $"{PublicLinks.GetAppBaseUrl(ctx.Request)}/RecoverPassword/{recoveryCode}";

            string bodyContent = $@"
            <h1 style='color: #333;'>Password Reset</h1>
            <p style='color: #666;'>Hello,</p>
            <p style='color: #666;'>You have requested a password reset for your account. To reset your password, please click the button below:</p>
            <a href='{link}' style='display: inline-block; padding: 10px 20px; background-color: #3498db; color: #fff; text-decoration: none; border-radius: 3px;'>Reset Password</a>
            <p style='color: #666;'>If you are unable to click the button, you can also copy and paste the following link into your browser:</p>
            <p style='color: #666;'><a href='{link}'>{link}</a></p>
            <p style='color: #666;'>Thank you,<br>Valour Team</p>";

            string emsg = EmailTemplateHelper.WrapInTemplate(bodyContent);

            string rawmsg = $"To reset your password, please go to the following link:\n{link}";

            using var emailTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(EmailTimeoutSeconds));
            var result = await EmailManager.SendEmailAsync(
                email,
                "Valour Password Recovery",
                rawmsg,
                emsg,
                cancellationToken: emailTimeout.Token);

            if (!result.IsSuccessStatusCode)
            {
                _logger.LogError($"Error issuing password reset email to {email}. Status code {result.StatusCode}.");
                return new(false, "Sorry! There was an issue sending the email. Try again?");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, "Sorry! An unexpected error occured. Try again?");
        }

        return new(true, "Success");
    }

    /// <summary>
    /// Sets a new password from a recovery code. A reset usually means the old
    /// password was lost or stolen, so every existing session is revoked.
    /// Following the emailed link also proves control of the address, so an
    /// unverified email becomes verified.
    /// </summary>
    public async Task<TaskResult> RecoveryUserAsync(PasswordRecoveryRequest request, PasswordRecovery recovery, Valour.Database.Credential cred)
    {
        List<string> revokedTokenIds;

        using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.PasswordRecoveries.Remove(await _db.PasswordRecoveries.FindAsync(recovery.Code));

            byte[] salt = PasswordManager.GenerateSalt();
            byte[] hash = PasswordManager.GetHashForPassword(request.Password, salt);

            cred.Salt = salt;
            cred.Secret = hash;
            cred.Iterations = PasswordManager.CurrentIterations;

            _db.Credentials.Update(cred);
            await _db.SaveChangesAsync();

            revokedTokenIds = await _db.AuthTokens
                .Where(x => x.UserId == cred.UserId)
                .Select(x => x.Id)
                .ToListAsync();

            await _db.AuthTokens.IgnoreQueryFilters()
                .Where(x => x.UserId == cred.UserId)
                .ExecuteDeleteAsync();

            await MarkEmailVerifiedAsync(cred.UserId);
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, "We're sorry. Something unexpected occured. Try again?");
        }

        await tran.CommitAsync();

        // Evict after commit to avoid re-cache race
        foreach (var tokenId in revokedTokenIds)
        {
            _tokenService.RemoveFromQuickCache(tokenId);
            _coreHub.ForceLogoutToken(tokenId);
        }

        // The owner just proved control of the email, so earlier guesses by
        // someone else should not keep them locked out.
        if (!string.IsNullOrWhiteSpace(cred.Identifier))
            await AuthAttemptThrottle.ResetAsync(_redis, AuthAttemptThrottle.PasswordKey(cred.Identifier), _logger);

        return new(true, "Success");
    }

    public int GetYearsOld(DateTime birthDate)
    {
        var now = DateTime.Today;
        var age = now.Year - birthDate.Year;
        if (birthDate > now.AddYears(-age)) age--;

        return age;
    }

    public async Task<TaskResult> SetUserComplianceData(long userId, DateTime birthDate)
    {
        if (GetYearsOld(birthDate) < 13)
            return new TaskResult(false, "You must be 13 or older to use Valour. Sorry!");

        birthDate = DateTime.SpecifyKind(birthDate, DateTimeKind.Utc); 
        
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return new TaskResult(false, "User not found");
        
        var userPrivateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == userId);
        if (userPrivateInfo is null)
            return new TaskResult(false, "User info not found");

        await using var trans = await _db.Database.BeginTransactionAsync();

        try
        {
            userPrivateInfo.BirthDate = birthDate;

            await _db.SaveChangesAsync();

            user.Compliance = true;

            await _db.SaveChangesAsync();

            await trans.CommitAsync();
        }
        catch (Exception)
        {
            await trans.RollbackAsync();
            return new TaskResult(false, "An unexpected error occured. Try again?");
        }
        
        return TaskResult.SuccessResult;
    }

    public async Task<User> GetUserAsync(string username, string tag)
        => (await _db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == username.ToLower() && x.Tag.ToLower() == tag.ToLower())).ToModel();

    
    /// <summary>
    /// Returns a user given the full name and tag: SpikeViper#0000
    /// </summary>
    /// <param name="username"></param>
    /// <returns></returns>
    public async Task<User> GetByNameAndTagAsync(string username)
    {
        // Names may themselves contain '#', tags never do, so split at the last '#'
        if (!UserUtils.TrySplitNameAndTag(username, out var name, out var tag))
        {
            return null;
        }

        return await GetUserAsync(name, tag);
    }
    
    public async Task<TaskResult<User>> UpdateAsync(User updatedUser)
    {
        var old = await _db.Users.FindAsync(updatedUser.Id); 
        if (old is null)
            return new TaskResult<User>(false, "Could not find user");

        old.Status = updatedUser.Status;
        old.UserStateCode = updatedUser.UserStateCode;
        old.HidePriorName = updatedUser.HidePriorName;

        // Validate and copy star colors
        if (updatedUser.StarColor1 != old.StarColor1 || updatedUser.StarColor2 != old.StarColor2)
        {
            if (old.SubscriptionType != UserSubscriptionTypes.StargazerPro.Name)
                return new TaskResult<User>(false, "Only Stargazer Pro subscribers can customize star colors.");

            if (!ColorHelpers.ValidateColorCode(updatedUser.StarColor1))
                return new TaskResult<User>(false, "Invalid star color 1.");

            if (!ColorHelpers.ValidateColorCode(updatedUser.StarColor2))
                return new TaskResult<User>(false, "Invalid star color 2.");

            old.StarColor1 = updatedUser.StarColor1;
            old.StarColor2 = updatedUser.StarColor2;
        }

        // Validate tag change
        if (updatedUser.Tag != old.Tag)
        {
            if (updatedUser.Tag.Length != 4)
            {
                return new TaskResult<User>(false, "Tag must be 4 characters long.");
            }
            
            // Ensure tag is alphanumeric
            foreach (var c in updatedUser.Tag)
            {
                if (!char.IsAsciiLetterOrDigit(c))
                {
                    return new TaskResult<User>(false, "Tag must be alphanumeric.");
                }
            }
            
            // Check if the tag is already taken
            if (await IsTagTaken(old.Name, updatedUser.Tag))
            {
                return new TaskResult<User>(false, "Tag already taken");
            }

            old.Tag = updatedUser.Tag;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        await _coreHub.NotifyUserChange(old.ToBroadcastModel());

        return new(true, "Success", old.ToModel());
    }

    public async Task<TaskResult> VerifyAsync(string code)
    {
        await using var tran = await _db.Database.BeginTransactionAsync();
        var confirmCode = await _db.EmailConfirmCodes.FirstOrDefaultAsync(x => x.Code == code && x.ExpiresAt > DateTime.UtcNow);
        if (confirmCode is null)
            return new TaskResult(false, "Code not found.");
        
        try
        {
            _db.EmailConfirmCodes.Remove(confirmCode);
            await _db.SaveChangesAsync();

            await MarkEmailVerifiedAsync(confirmCode.UserId);

            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }
        
        return new(true, "Success");
    }

    /// <summary>
    /// Marks the user's email as verified. A pending referral reward is paid only
    /// when this call is the one that flips the flag, so it is paid exactly once
    /// even if two verifications race. Runs inside the caller's transaction.
    /// </summary>
    private async Task MarkEmailVerifiedAsync(long userId)
    {
        var changed = await _db.PrivateInfos
            .Where(x => x.UserId == userId && !x.Verified)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Verified, true));

        if (changed > 0)
            await PayReferralRewardAsync(userId);
    }

    /// <summary>
    /// Pays the referrer of the given user, if any. Rewards are only paid once the
    /// referred account has a verified email, so throwaway addresses cannot farm
    /// credits. The reward halves every few rewarded referrals in a 30 day window
    /// and stops at a monthly cap. Runs inside the caller's transaction.
    /// </summary>
    public async Task PayReferralRewardAsync(long userId)
    {
        var refer = await _db.Referrals.FirstOrDefaultAsync(x => x.UserId == userId);
        if (refer is null || refer.Reward > 0)
            return;

        var cutoff = DateTime.UtcNow.AddDays(-30);
        var rewardedThisMonth = await _db.Referrals.CountAsync(r =>
            r.ReferrerId == refer.ReferrerId &&
            r.UserId != userId &&
            r.Created > cutoff &&
            _db.PrivateInfos.Any(p => p.UserId == r.UserId && p.Verified));

        if (rewardedThisMonth >= MaxRewardedReferralsPerMonth)
            return;

        // Reward is halved every few referrals in the month to prevent a
        // streamer from wrecking the economy
        var reward = 50.0m / (1 + (rewardedThisMonth / ReferralRewardHalvingInterval));

        var referAccount = await _db.EcoAccounts.FirstOrDefaultAsync(x =>
            x.UserId == refer.ReferrerId &&
            x.CurrencyId == ISharedCurrency.ValourCreditsId &&
            x.AccountType == AccountType.User);
        if (referAccount is null)
            return;

        referAccount.BalanceValue += reward;
        refer.Reward = reward;
        await _db.SaveChangesAsync();
    }

    public async Task<TaskResult> Logout()
    {
        try
        {
            var key = _tokenService.GetAuthKey();
            var dbToken = await _db.AuthTokens.FindAsync(key);
            if (dbToken is not null)
            {
                _db.AuthTokens.Remove(dbToken);
                await _db.SaveChangesAsync();

                // Evict AFTER the delete is committed; evicting first allows a
                // concurrent request to re-cache the token from the still-live
                // DB row, leaving a permanently stale cache entry
                _tokenService.RemoveFromQuickCache(key);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success");
    }

    /// <summary>
    /// Gets all tokens for a user
    /// </summary>
    /// <summary>
    /// Lists the user's sessions for session management. Returns a projection that
    /// omits the token id: the id IS the bearer secret, so returning it here would
    /// let any token read every other session key on the account.
    /// </summary>
    public async Task<List<AuthTokenInfo>> GetUserTokensAsync(long userId, string currentTokenId)
    {
        var tokens = await _db.AuthTokens
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.TimeCreated)
            .Select(x => new { x.Id, x.AppId, x.Scope, x.TimeCreated, x.TimeExpires })
            .ToListAsync();

        return tokens.Select(x => new AuthTokenInfo()
        {
            Handle = AuthTokenInfo.GetHandle(x.Id),
            AppId = x.AppId,
            Scope = x.Scope,
            TimeCreated = x.TimeCreated,
            TimeExpires = x.TimeExpires,
            IsCurrent = currentTokenId is not null && x.Id == currentTokenId,
        }).ToList();
    }

    /// <summary>
    /// Revokes a specific session, addressed by its public handle rather than by
    /// the token secret (which is never given to clients). The handle is a hash,
    /// so the match is done in memory over the caller's own tokens.
    /// </summary>
    public async Task<TaskResult> RevokeTokenAsync(long userId, string handle)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(handle))
                return new TaskResult(false, "Token not found");

            var userTokens = await _db.AuthTokens
                .Where(x => x.UserId == userId)
                .ToListAsync();

            var token = userTokens.FirstOrDefault(x =>
                string.Equals(AuthTokenInfo.GetHandle(x.Id), handle, StringComparison.OrdinalIgnoreCase));

            if (token is null)
                return new TaskResult(false, "Token not found");

            var tokenId = token.Id;

            _db.AuthTokens.Remove(token);
            await _db.SaveChangesAsync();

            // Evict after commit to avoid re-cache race
            _tokenService.RemoveFromQuickCache(tokenId);
            _coreHub.ForceLogoutToken(tokenId);

            return new TaskResult(true, "Token revoked successfully");
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new TaskResult(false, e.Message);
        }
    }

    /// <summary>
    /// Revokes all active (non-expired) tokens for a user except the current one.
    /// Expired tokens are left alone; use RevokeExpiredTokensAsync to clear those.
    /// </summary>
    public async Task<TaskResult> RevokeAllOtherTokensAsync(long userId, string currentTokenId)
    {
        try
        {
            var tokens = await _db.AuthTokens
                .Where(x => x.UserId == userId && x.Id != currentTokenId && x.TimeExpires > DateTime.UtcNow)
                .ToListAsync();

            _db.AuthTokens.RemoveRange(tokens);
            await _db.SaveChangesAsync();

            // Evict after commit to avoid re-cache race
            foreach (var token in tokens)
            {
                _tokenService.RemoveFromQuickCache(token.Id);
                _coreHub.ForceLogoutToken(token.Id);
            }

            return new TaskResult(true, $"Revoked {tokens.Count} tokens");
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new TaskResult(false, e.Message);
        }
    }

    /// <summary>
    /// Revokes all expired tokens for a user
    /// </summary>
    public async Task<TaskResult> RevokeExpiredTokensAsync(long userId)
    {
        try
        {
            var tokens = await _db.AuthTokens
                .Where(x => x.UserId == userId && x.TimeExpires < DateTime.UtcNow)
                .ToListAsync();

            _db.AuthTokens.RemoveRange(tokens);
            await _db.SaveChangesAsync();

            // Evict after commit to avoid re-cache race
            foreach (var token in tokens)
            {
                _tokenService.RemoveFromQuickCache(token.Id);
            }

            return new TaskResult(true, $"Revoked {tokens.Count} expired session(s)");
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new TaskResult(false, e.Message);
        }
    }

    /// <summary>
    /// Rotates the current session token after a privilege change (password change, MFA change, etc.)
    /// Creates a new token and revokes all other tokens for security.
    /// </summary>
    /// <param name="ctx">The HTTP context for generating the new token</param>
    /// <param name="userId">The user ID</param>
    /// <param name="currentTokenId">The current token ID to revoke</param>
    /// <returns>The new auth token</returns>
    public async Task<TaskResult<AuthToken>> RotateSessionTokenAsync(HttpContext ctx, long userId, string currentTokenId)
    {
        try
        {
            // Create a new token first
            var newTokenResult = await GetTokenAfterLoginAsync(ctx, userId);
            if (!newTokenResult.Success)
                return newTokenResult;

            // Revoke all other tokens including the old current token
            var tokens = await _db.AuthTokens
                .Where(x => x.UserId == userId && x.Id != newTokenResult.Data.Id)
                .ToListAsync();

            _db.AuthTokens.RemoveRange(tokens);
            await _db.SaveChangesAsync();

            // Evict after commit to avoid re-cache race. Live connections of the
            // other sessions are closed too; the caller's own session is left
            // alone because it receives the replacement token in the response.
            foreach (var token in tokens)
            {
                _tokenService.RemoveFromQuickCache(token.Id);
                if (token.Id != currentTokenId)
                    _coreHub.ForceLogoutToken(token.Id);
            }

            _logger.LogInformation("Session token rotated for user {UserId}. Revoked {Count} old tokens.", userId, tokens.Count);

            return newTokenResult;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to rotate session token for user {UserId}", userId);
            return new TaskResult<AuthToken>(false, e.Message);
        }
    }


    /// <summary>
    /// Returns the user for the current context
    /// </summary>

    public async Task<User> GetCurrentUserAsync()
    {
        var token = await _tokenService.GetCurrentTokenAsync();
        if (token is null) return null;
        _currentUser = await GetAsync(token.UserId);
        return _currentUser;
    }

    /// <summary>
    /// Updates one platform badge's public visibility by flipping its durable
    /// catalog bit in the user's hidden-badge mask.
    /// </summary>
    public async Task<TaskResult<User>> SetBadgeVisibilityAsync(
        long userId, PlatformBadge badge, bool visible)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult<User>.FromFailure("User not found.");

        if (!PlatformBadgeCatalog.Definitions.ContainsKey(badge))
            return TaskResult<User>.FromFailure("That badge is not configurable.");

        if (!PlatformBadgeCatalog.IsEarned(user, badge))
            return TaskResult<User>.FromFailure("You do not have that badge.");

        user.HiddenBadgeFlags = BadgeVisibility.SetVisible(
            user.HiddenBadgeFlags, (long)badge, visible);
        await _db.SaveChangesAsync();

        var model = user.ToModel();
        await _coreHub.NotifyUserChange(model);
        return TaskResult<User>.FromData(model);
    }
    
    /// <summary>
    /// Returns the user id for the current context
    /// </summary>

    public async Task<long> GetCurrentUserIdAsync()
    {
        var token = await _tokenService.GetCurrentTokenAsync();
        return token?.UserId ?? long.MinValue;
    }
    
    /// <summary>
    /// Returns the amount of planets owned by the given user
    /// </summary>
    public Task<int> GetOwnedPlanetCount(long userId) => 
        _db.Planets.CountAsync(x => x.OwnerId == userId);
    
    /// <summary>
    /// Returns the amount of planets joined by the given user
    /// </summary>
    public Task<int> GetJoinedPlanetCount(long userId) => 
        _db.PlanetMembers.CountAsync(x => x.UserId == userId);

    private async Task RecordPasswordFailureAsync(string throttleKey, string clientThrottleKey)
    {
        await AuthAttemptThrottle.RecordFailureAsync(_redis, throttleKey, _logger);
        if (clientThrottleKey is not null)
            await AuthAttemptThrottle.RecordFailureAsync(_redis, clientThrottleKey, _logger);
    }

    public async Task<TaskResult<User>> ValidateCredentialAsync(string credential_type, string identifier, string secret)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(secret))
            return new TaskResult<User>(false, "The credentials were incorrect.", null);

        // Failures are counted per submitted identifier, so this is the same for
        // real and unknown accounts and cannot be used to enumerate them. A low
        // limit applies per client address and a high one across all addresses.
        var throttleKey = AuthAttemptThrottle.PasswordKey(identifier);
        var httpContext = _httpContextAccessor.HttpContext;
        var clientThrottleKey = httpContext is null
            ? null
            : AuthAttemptThrottle.PasswordKey(identifier, ClientAddressResolver.GetRateLimitKey(httpContext));

        if (await AuthAttemptThrottle.IsLockedAsync(_redis, throttleKey, AuthAttemptThrottle.AccountPasswordFailureLimit, _logger) ||
            (clientThrottleKey is not null &&
             await AuthAttemptThrottle.IsLockedAsync(_redis, clientThrottleKey, AuthAttemptThrottle.ClientPasswordFailureLimit, _logger)))
            return new TaskResult<User>(false, AuthAttemptThrottle.LockedMessage, null, code: AccountThrottledCode);

        // Find the credential that matches the identifier and type
        Valour.Database.Credential credential = await _db.Credentials.FirstOrDefaultAsync(
            x => string.Equals(credential_type.ToUpper(), x.CredentialType.ToUpper()) &&
                    string.Equals(identifier.ToUpper(), x.Identifier.ToUpper()));

        if (credential == null)
        {
            // Spend the same hashing work as a real check so the response time
            // does not reveal whether an account exists for this identifier.
            PasswordManager.GetHashForPassword(secret, DecoySalt);
            await RecordPasswordFailureAsync(throttleKey, clientThrottleKey);
            return new TaskResult<User>(false, "The credentials were incorrect.", null);
        }

        // Rows written before iteration tracking existed have 0 here and were
        // hashed with the legacy count.
        var iterations = credential.Iterations > 0
            ? credential.Iterations
            : PasswordManager.LegacyIterations;

        // Use salt to validate secret hash
        byte[] hash = PasswordManager.GetHashForPassword(secret, credential.Salt, iterations);

        if (!PasswordManager.HashesMatch(hash, credential.Secret))
        {
            await RecordPasswordFailureAsync(throttleKey, clientThrottleKey);
            return new TaskResult<User>(false, "The credentials were incorrect.", null);
        }

        await AuthAttemptThrottle.ResetAsync(_redis, throttleKey, _logger);
        if (clientThrottleKey is not null)
            await AuthAttemptThrottle.ResetAsync(_redis, clientThrottleKey, _logger);

        // The password is correct and we have the plaintext here, so this is the
        // only moment we can raise the work factor without a reset.
        if (iterations < PasswordManager.CurrentIterations)
        {
            try
            {
                credential.Secret = PasswordManager.GetHashForPassword(secret, credential.Salt);
                credential.Iterations = PasswordManager.CurrentIterations;
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                // An upgrade failure must never block a valid login.
                _logger.LogError(e, "Failed to upgrade password hash for user {UserId}", credential.UserId);
            }
        }

        User user = await GetAsync(credential.UserId);

        if (user.Disabled)
        {
            return new TaskResult<User>(false, "This account has been disabled", null, code: AccountDisabledCode);
        }

        return new TaskResult<User>(true, "Succeeded", user);
    }

    public async Task<TaskResult<AuthToken>> GetTokenAfterLoginAsync(HttpContext ctx, long userId)
    {
        try
        {
            // Always create a new token for each login
            var token = new AuthToken()
            {
                AppId = "VALOUR",
                Id = "val-" + Guid.NewGuid().ToString(),
                TimeCreated = DateTime.UtcNow,
                TimeExpires = DateTime.UtcNow.AddDays(7),
                Scope = UserPermissions.FullControl.Value,
                UserId = userId,
                IssuedAddress = ClientAddressResolver.GetClientAddress(ctx),
            }.ToDatabase();

            await _db.AuthTokens.AddAsync(token);
            await _db.SaveChangesAsync();

            return new(true, "Success", token.ToModel());
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }
    }

    public async Task<TaskResult> ChangePasswordAsync(long userId, string newPassword)
    {
        var cred = await _db.Credentials.FirstOrDefaultAsync(x => x.UserId == userId && x.CredentialType == CredentialType.PASSWORD);
        
        if (cred is null)
            return new TaskResult(false, "Credential not found.");
        
        var salt = PasswordManager.GenerateSalt();
        var hash = PasswordManager.GetHashForPassword(newPassword, salt);
        
        cred.Salt = salt;
        cred.Secret = hash;
        cred.Iterations = PasswordManager.CurrentIterations;
        
        try
        {
            _db.Credentials.Update(cred);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new TaskResult(false, e.Message);
        }
        
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Updates the user's username, and changes their tag if they are not a Stargazer.
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="newUsername"></param>
    /// <returns>TaskResult(success, message)</returns>
    public async Task<TaskResult> ChangeUsernameAsync(long userId, string newUsername)
    {
        var user = await _db.Users.FindAsync(userId);
        // Verify user exists
        if (user is null)
            return new TaskResult(false, "User not found.");
        // Verify new username is ACTUALLY NEW
        if (user.Name == newUsername)
            return new TaskResult(false, "New username matches your existing username.");
        
        // Verify username is allowed
        var validResult = UserUtils.TestUsername(newUsername);
        if (!validResult.Success)
            return validResult;
        
        // If user is a Stargazer, verify the new username/tag combo is unique
        if (user.SubscriptionType is not null)
        {
            // Ensure it's been 7 days since the last name change
            if (user.NameChangeTime is not null && user.NameChangeTime.Value.AddDays(7) > DateTime.UtcNow)
                return new TaskResult(false, "You can only change your username once every 7 days.");
            
            if (await _db.Users.AnyAsync(x => x.Name.ToLower() == newUsername.ToLower() && x.Tag.ToLower() == user.Tag.ToLower()))
                return new TaskResult(false, "Username and tag already taken, please change the username or your tag and try again.");
        }
        // If user is NOT a Stargazer, assign new tag and verify it is unique with the new username
        if (user.SubscriptionType is null)
        {
            // Ensure it's been 30 days since the last name change
            if (user.NameChangeTime is not null && user.NameChangeTime.Value.AddDays(30) > DateTime.UtcNow)
                return new TaskResult(false, "You can only change your username once every 30 days.");
            
            var loop = true;
            while (loop)
            {
                var tag = await GetUniqueTag(newUsername);
                if (!await _db.Users.AnyAsync(x => x.Name.ToLower() == newUsername.ToLower() && x.Tag.ToLower() == tag.ToLower()))
                {
                    user.Tag = tag;
                    loop = false;
                }
            }
        }

        user.PriorName = user.Name;
        user.Name = newUsername;
        user.NameChangeTime = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new TaskResult(false, e.Message);
        }
        
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Nuke it. Bots owned by the user are deleted first, because a bot must
    /// not keep acting (with its long-lived token) after its owner is gone.
    /// </summary>
    public async Task<TaskResult> HardDelete(User user)
    {
        if (!user.Bot)
        {
            var botIds = await _db.Users.IgnoreQueryFilters()
                .Where(x => x.OwnerId == user.Id && x.Bot)
                .Select(x => x.Id)
                .ToListAsync();

            // Each bot is deleted in its own transaction. If one fails, stop
            // before deleting the owner so no bot is left without one.
            foreach (var botId in botIds)
            {
                var bot = await GetAsync(botId);
                if (bot is null)
                    continue;

                var botResult = await HardDelete(bot);
                if (!botResult.Success)
                    return TaskResult.FromFailure($"Could not delete bot {bot.Name}: {botResult.Message}");
            }
        }

        // Billing stops before any data is removed. If this fails the account
        // is left intact, so a deleted account can never keep being charged.
        var billingResult = await CancelStripeSubscriptionsAsync(user.Id);
        if (!billingResult.Success)
            return billingResult;

        await using var tran = await _db.Database.BeginTransactionAsync();

        var dbUser = await _db.Users.FindAsync(user.Id);
        if (dbUser is null)
            return TaskResult.FromFailure("User not found.");

        List<string> revokedTokenIds = [];
        List<string> deletedUploadHashes = [];
        List<long> ownedAppIds = [];

        try
        {
            var directChannelIds = await _db.Channels
                .IgnoreQueryFilters()
                .Where(x => x.ChannelType == ChannelTypeEnum.DirectChat &&
                            x.Members.Any(m => m.UserId == dbUser.Id))
                .Select(x => x.Id)
                .ToListAsync();

            var directCallIds = await _db.DirectCallMembers
                .Where(x => x.UserId == dbUser.Id)
                .Select(x => x.CallId)
                .Distinct()
                .ToListAsync();

            // Messages still waiting to be flushed would reference the deleted user and memberships
            PlanetMessageWorker.RemoveMessages(x => x.AuthorUserId == dbUser.Id);

            var authoredMessageIds = await _db.Messages
                .IgnoreQueryFilters()
                .Where(x => x.AuthorUserId == dbUser.Id)
                .Select(x => x.Id)
                .ToListAsync();

            var directMessageIds = new List<long>();
            if (directChannelIds.Count > 0)
            {
                directMessageIds = await _db.Messages
                    .IgnoreQueryFilters()
                    .Where(x => directChannelIds.Contains(x.ChannelId))
                    .Select(x => x.Id)
                    .ToListAsync();
            }

            var deletedMessageIds = authoredMessageIds
                .Concat(directMessageIds)
                .Distinct()
                .ToList();

            var planetMemberIds = await _db.PlanetMembers
                .IgnoreQueryFilters()
                .Where(x => x.UserId == dbUser.Id)
                .Select(x => x.Id)
                .ToListAsync();

            var automodTriggerIds = new List<Guid>();
            if (planetMemberIds.Count > 0)
            {
                automodTriggerIds = await _db.AutomodTriggers
                    .IgnoreQueryFilters()
                    .Where(x => planetMemberIds.Contains(x.MemberAddedBy))
                    .Select(x => x.Id)
                    .ToListAsync();
            }

            revokedTokenIds = await _db.AuthTokens.IgnoreQueryFilters()
                .Where(x => x.UserId == dbUser.Id)
                .Select(x => x.Id)
                .ToListAsync();

            await _db.AuthTokens.IgnoreQueryFilters()
                .Where(x => x.UserId == dbUser.Id)
                .ExecuteDeleteAsync();

            await _db.UserProfiles.IgnoreQueryFilters()
                .Where(x => x.Id == dbUser.Id)
                .ExecuteDeleteAsync();

            await _db.UserPreferences.IgnoreQueryFilters()
                .Where(x => x.Id == dbUser.Id)
                .ExecuteDeleteAsync();

            if (directCallIds.Count > 0)
            {
                await _db.DirectCallMembers
                    .Where(x => directCallIds.Contains(x.CallId))
                    .ExecuteDeleteAsync();
                await _db.DirectCalls
                    .Where(x => directCallIds.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }

            foreach (var entry in _db.ChangeTracker.Entries<Valour.Database.AuthToken>()
                         .Where(x => x.Entity.UserId == dbUser.Id)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            foreach (var entry in _db.ChangeTracker.Entries<Valour.Database.UserProfile>()
                         .Where(x => x.Entity.Id == dbUser.Id)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            foreach (var entry in _db.ChangeTracker.Entries<Valour.Database.UserPreferences>()
                         .Where(x => x.Entity.Id == dbUser.Id)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            // Remove message dependents and historical pointers BEFORE deleting messages
            // to avoid FK constraint violations on installations with stricter constraints.
            if (deletedMessageIds.Count > 0)
            {
                await _db.Messages.IgnoreQueryFilters()
                    .Where(x => x.ReplyToId.HasValue && deletedMessageIds.Contains(x.ReplyToId.Value))
                    .ExecuteUpdateAsync(x => x.SetProperty(m => m.ReplyToId, (long?)null));

                await _db.MessageReactions.IgnoreQueryFilters()
                    .Where(x => deletedMessageIds.Contains(x.MessageId))
                    .ExecuteDeleteAsync();

                await _db.MessageAttachments.IgnoreQueryFilters()
                    .Where(x => deletedMessageIds.Contains(x.MessageId))
                    .ExecuteDeleteAsync();

                await _db.MessageMentions.IgnoreQueryFilters()
                    .Where(x => deletedMessageIds.Contains(x.MessageId))
                    .ExecuteDeleteAsync();

                await _db.Reports.IgnoreQueryFilters()
                    .Where(x => x.MessageId.HasValue && deletedMessageIds.Contains(x.MessageId.Value))
                    .ExecuteUpdateAsync(x => x.SetProperty(r => r.MessageId, (long?)null));

                await _db.ModerationAuditLogs.IgnoreQueryFilters()
                    .Where(x => x.MessageId.HasValue && deletedMessageIds.Contains(x.MessageId.Value))
                    .ExecuteUpdateAsync(x => x.SetProperty(l => l.MessageId, (long?)null));

                await _db.AutomodLogs.IgnoreQueryFilters()
                    .Where(x => x.MessageId.HasValue && deletedMessageIds.Contains(x.MessageId.Value))
                    .ExecuteUpdateAsync(x => x.SetProperty(l => l.MessageId, (long?)null));

                await _db.AutomodActions.IgnoreQueryFilters()
                    .Where(x => x.MessageId.HasValue && deletedMessageIds.Contains(x.MessageId.Value))
                    .ExecuteUpdateAsync(x => x.SetProperty(a => a.MessageId, (long?)null));
            }

            await _db.MessageReactions.IgnoreQueryFilters()
                .Where(x => x.AuthorUserId == dbUser.Id)
                .ExecuteDeleteAsync();

            if (planetMemberIds.Count > 0)
            {
                await _db.Messages.IgnoreQueryFilters()
                    .Where(x => x.AuthorMemberId.HasValue && planetMemberIds.Contains(x.AuthorMemberId.Value))
                    .ExecuteUpdateAsync(x => x.SetProperty(m => m.AuthorMemberId, (long?)null));

                await _db.MessageReactions.IgnoreQueryFilters()
                    .Where(x => x.AuthorMemberId.HasValue && planetMemberIds.Contains(x.AuthorMemberId.Value))
                    .ExecuteUpdateAsync(x => x.SetProperty(r => r.AuthorMemberId, (long?)null));
            }

            if (deletedMessageIds.Count > 0)
            {
                await _db.Messages.IgnoreQueryFilters()
                    .Where(x => deletedMessageIds.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }

            // Some older databases still enforce report FKs from legacy schema definitions.
            // Clear those rows explicitly before deleting the user or any DM channels.
            await _db.Reports.IgnoreQueryFilters()
                .Where(x => x.ReportingUserId == dbUser.Id)
                .ExecuteDeleteAsync();

            await _db.Reports.IgnoreQueryFilters()
                .Where(x => x.ReportedUserId == dbUser.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.ReportedUserId, (long?)null));

            await _db.Reports.IgnoreQueryFilters()
                .Where(x => x.ResolvedById == dbUser.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.ResolvedById, (long?)null));

            await _db.ModerationAuditLogs.IgnoreQueryFilters()
                .Where(x => x.ActorUserId == dbUser.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(l => l.ActorUserId, (long?)null));

            await _db.ModerationAuditLogs.IgnoreQueryFilters()
                .Where(x => x.TargetUserId == dbUser.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(l => l.TargetUserId, (long?)null));

            if (planetMemberIds.Count > 0)
            {
                await _db.ModerationAuditLogs.IgnoreQueryFilters()
                    .Where(x => x.TargetMemberId.HasValue && planetMemberIds.Contains(x.TargetMemberId.Value))
                    .ExecuteUpdateAsync(x => x.SetProperty(l => l.TargetMemberId, (long?)null));

                await _db.AutomodLogs.IgnoreQueryFilters()
                    .Where(x => planetMemberIds.Contains(x.MemberId) || automodTriggerIds.Contains(x.TriggerId))
                    .ExecuteDeleteAsync();

                await _db.AutomodActions.IgnoreQueryFilters()
                    .Where(x => planetMemberIds.Contains(x.MemberAddedBy) ||
                                planetMemberIds.Contains(x.TargetMemberId) ||
                                automodTriggerIds.Contains(x.TriggerId))
                    .ExecuteDeleteAsync();

                if (automodTriggerIds.Count > 0)
                {
                    await _db.AutomodTriggers.IgnoreQueryFilters()
                        .Where(x => automodTriggerIds.Contains(x.Id))
                        .ExecuteDeleteAsync();
                }
            }

            if (directChannelIds.Count > 0)
            {
                await _db.Reports.IgnoreQueryFilters()
                    .Where(x => x.ChannelId.HasValue && directChannelIds.Contains(x.ChannelId.Value))
                    .ExecuteDeleteAsync();
            }
            
            // Detach this user's uploaded files from any remaining messages before removing CDN rows.
            var attachmentItemIds = await _db.CdnBucketItems.IgnoreQueryFilters()
                .Where(x => x.UserId == user.Id)
                .Select(x => x.Id)
                .ToListAsync();

            // Uploads quarantined by the media safety check are evidence that
            // child-safety law requires us to preserve, so their records stay.
            deletedUploadHashes = await _db.CdnBucketItems.IgnoreQueryFilters()
                .Where(x => x.UserId == user.Id && x.SafetyQuarantinedAt == null)
                .Select(x => x.Hash)
                .Distinct()
                .ToListAsync();

            if (attachmentItemIds.Count > 0)
            {
                await _db.MessageAttachments.IgnoreQueryFilters()
                    .Where(x => attachmentItemIds.Contains(x.CdnBucketItemId))
                    .ExecuteUpdateAsync(x => x
                        .SetProperty(a => a.CdnBucketItemId, (string)null)
                        .SetProperty(a => a.Location, Valour.Sdk.Models.MessageAttachment.MissingLocation)
                        .SetProperty(a => a.Type, MessageAttachmentType.File)
                        .SetProperty(a => a.MimeType, "application/octet-stream")
                        .SetProperty(a => a.FileName, "Attachment not found")
                        .SetProperty(a => a.Width, 0)
                        .SetProperty(a => a.Height, 0)
                        .SetProperty(a => a.Inline, false)
                        .SetProperty(a => a.Missing, true)
                        .SetProperty(a => a.Data, (string)null)
                        .SetProperty(a => a.OpenGraphData, (string)null));

                await _db.ThreadAttachments.IgnoreQueryFilters()
                    .Where(x => attachmentItemIds.Contains(x.CdnBucketItemId))
                    .ExecuteUpdateAsync(x => x
                        .SetProperty(a => a.CdnBucketItemId, (string)null)
                        .SetProperty(a => a.Location, Valour.Sdk.Models.MessageAttachment.MissingLocation)
                        .SetProperty(a => a.Type, MessageAttachmentType.File)
                        .SetProperty(a => a.MimeType, "application/octet-stream")
                        .SetProperty(a => a.FileName, "Attachment not found")
                        .SetProperty(a => a.Width, 0)
                        .SetProperty(a => a.Height, 0)
                        .SetProperty(a => a.Inline, false)
                        .SetProperty(a => a.Missing, true)
                        .SetProperty(a => a.Data, (string)null)
                        .SetProperty(a => a.OpenGraphData, (string)null));
            }

            await _db.CdnBucketItems.IgnoreQueryFilters()
                .Where(x => x.UserId == user.Id && x.SafetyQuarantinedAt == null)
                .ExecuteDeleteAsync();

            await DeleteCommunityContentAsync(dbUser.Id, planetMemberIds);

            // Bulk updates/deletes bypass EF's change tracker. Clear before switching
            // back to tracked removals so previously loaded related rows are not saved
            // again later in the transaction.
            _db.ChangeTracker.Clear();
            dbUser = await _db.Users.FindAsync(user.Id);
            if (dbUser is null)
            {
                await tran.RollbackAsync();
                return TaskResult.FromFailure("User not found.");
            }

            // Channel states
            var states = _db.UserChannelStates.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.UserChannelStates.RemoveRange(states);
            
            await _db.SaveChangesAsync();
            
            // Channel membership
            var affectedGroupChannelIds = await _db.ChannelMembers.IgnoreQueryFilters()
                .Where(x => x.UserId == dbUser.Id && x.Channel.ChannelType == ChannelTypeEnum.GroupChat)
                .Select(x => x.ChannelId)
                .Distinct()
                .ToListAsync();
            var dchannelMembers = _db.ChannelMembers.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.ChannelMembers.RemoveRange(dchannelMembers);

            await _db.SaveChangesAsync();

            foreach (var groupChannelId in affectedGroupChannelIds)
            {
                if (await _db.ChannelMembers.AnyAsync(x => x.ChannelId == groupChannelId && x.IsAdmin))
                    continue;

                var replacementAdmin = await _db.ChannelMembers
                    .Where(x => x.ChannelId == groupChannelId)
                    .OrderBy(x => x.Id)
                    .FirstOrDefaultAsync();
                if (replacementAdmin is not null)
                    replacementAdmin.IsAdmin = true;
            }
            await _db.SaveChangesAsync();
            
            // End-to-end encryption. The account's key log is kept: it holds
            // only public keys, and other people's signed membership logs and
            // messages refer to it. Everything that could open anything goes.
            await _db.E2eeUserKeyBoxes.Where(x => x.UserId == dbUser.Id).ExecuteDeleteAsync();
            await _db.E2eeDeviceLinkSessions.Where(x => x.UserId == dbUser.Id).ExecuteDeleteAsync();
            await _db.E2eeKeyRequests.Where(x => x.UserId == dbUser.Id).ExecuteDeleteAsync();
            await _db.E2eeChannelKeyBoxes.Where(x => x.UserId == dbUser.Id).ExecuteDeleteAsync();
            await _db.MessageProofs.Where(x => x.AuthorUserId == dbUser.Id).ExecuteDeleteAsync();

            // Direct Message Channels
            if (directChannelIds.Count > 0)
            {
                await _db.E2eeChannelKeyBoxes.Where(x => directChannelIds.Contains(x.ChannelId)).ExecuteDeleteAsync();
                await _db.E2eeChannelKeyGenerations.Where(x => directChannelIds.Contains(x.ChannelId)).ExecuteDeleteAsync();
                await _db.E2eeKeyRequests.Where(x => directChannelIds.Contains(x.ChannelId)).ExecuteDeleteAsync();
                await _db.MessageProofs.Where(x => directChannelIds.Contains(x.ChannelId)).ExecuteDeleteAsync();

                await _db.Messages.IgnoreQueryFilters()
                    .Where(x => directChannelIds.Contains(x.ChannelId))
                    .ExecuteDeleteAsync();

                await _db.UserChannelStates.IgnoreQueryFilters()
                    .Where(x => directChannelIds.Contains(x.ChannelId))
                    .ExecuteDeleteAsync();

                await _db.Notifications.IgnoreQueryFilters()
                    .Where(x => x.ChannelId.HasValue && directChannelIds.Contains(x.ChannelId.Value))
                    .ExecuteDeleteAsync();

                await _db.ChannelMembers.IgnoreQueryFilters()
                    .Where(x => directChannelIds.Contains(x.ChannelId))
                    .ExecuteDeleteAsync();

                await _db.Channels.IgnoreQueryFilters()
                    .Where(x => directChannelIds.Contains(x.Id))
                    .ExecuteDeleteAsync();
            }

            // Remove friends and friend requests
            var requests = _db.UserFriends.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id || x.FriendId == dbUser.Id);
            _db.UserFriends.RemoveRange(requests);

            await _db.UserBlocks.IgnoreQueryFilters()
                .Where(x => x.UserId == dbUser.Id || x.BlockedUserId == dbUser.Id)
                .ExecuteDeleteAsync();

            await _db.ThemeVotes.IgnoreQueryFilters()
                .Where(x => x.UserId == dbUser.Id)
                .ExecuteDeleteAsync();

            await _db.PlanetEmojis.IgnoreQueryFilters()
                .Where(x => x.CreatorUserId == dbUser.Id)
                .ExecuteDeleteAsync();

            foreach (var entry in _db.ChangeTracker.Entries<Valour.Database.PlanetEmoji>()
                         .Where(x => x.Entity.CreatorUserId == dbUser.Id)
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }

            // Remove email confirm codes
            var codes = _db.EmailConfirmCodes.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.EmailConfirmCodes.RemoveRange(codes);
            
            // Remove user emails
            var emails = _db.PrivateInfos.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.PrivateInfos.RemoveRange(emails);

            // Remove credentials
            var creds = _db.Credentials.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.Credentials.RemoveRange(creds);

            var recovs = _db.PasswordRecoveries.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.PasswordRecoveries.RemoveRange(recovs);

            // Remove multi-factor auth records
            var multiAuths = _db.MultiAuths.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.MultiAuths.RemoveRange(multiAuths);

            await _db.SaveChangesAsync();

            // Remove OAuth apps owned by this user
            ownedAppIds = await _db.OauthApps.IgnoreQueryFilters()
                .Where(x => x.OwnerId == dbUser.Id)
                .Select(x => x.Id)
                .ToListAsync();
            var oauthApps = _db.OauthApps.IgnoreQueryFilters().Where(x => x.OwnerId == dbUser.Id);
            _db.OauthApps.RemoveRange(oauthApps);

            await _db.Users.IgnoreQueryFilters()
                .Where(x => x.OwnerId == dbUser.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.OwnerId, (long?)null));

            // Remove user subscriptions
            var subscriptions = _db.UserSubscriptions.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.UserSubscriptions.RemoveRange(subscriptions);

            // Remove tenor favorites
            var tenorFavorites = _db.TenorFavorites.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.TenorFavorites.RemoveRange(tenorFavorites);

            var gifFavorites = _db.GifFavorites.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.GifFavorites.RemoveRange(gifFavorites);

            var channelFavorites = _db.ChannelFavorites.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.ChannelFavorites.RemoveRange(channelFavorites);

            await _db.SaveChangesAsync();
            
            // Remove eco stuff
            await _db.Transactions.IgnoreQueryFilters()
                .Where(x => x.ForcedBy == dbUser.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.ForcedBy, (long?)null));

            var transactions = _db.Transactions.IgnoreQueryFilters().Where(x => x.UserFromId == dbUser.Id || x.UserToId == dbUser.Id);
            _db.Transactions.RemoveRange(transactions);

            var ecoAccounts = _db.EcoAccounts.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.EcoAccounts.RemoveRange(ecoAccounts);

            await _db.SaveChangesAsync();

            // Message reactions were already removed before message deletion to avoid FK violations.

            // Remove planet membership
            var members = _db.PlanetMembers.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.PlanetMembers.RemoveRange(members);

            await _db.SaveChangesAsync();

            // Referrals
            var refer = _db.Referrals.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id || x.ReferrerId == dbUser.Id);
            _db.Referrals.RemoveRange(refer);

            // Notifications
            var nots = _db.PushNotificationSubscriptions.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.PushNotificationSubscriptions.RemoveRange(nots);
            
            // Also notifications
            var noots  = _db.Notifications.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.Notifications.RemoveRange(noots);

            // Bans
            var bans = _db.PlanetBans.IgnoreQueryFilters().Where(x => x.IssuerId == dbUser.Id || x.TargetId == dbUser.Id);
            _db.PlanetBans.RemoveRange(bans);

            // Planet invites
            var invites = _db.PlanetInvites.IgnoreQueryFilters().Where(x => x.IssuerId == dbUser.Id);
            _db.PlanetInvites.RemoveRange(invites);

            var dbConnection = _db.Database.GetDbConnection();
            var shouldCloseConnection = dbConnection.State != ConnectionState.Open;
            if (shouldCloseConnection)
                await dbConnection.OpenAsync();

            try
            {
                await using var existsCommand = dbConnection.CreateCommand();
                existsCommand.CommandText = """
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'primary_node_connections'
                    LIMIT 1
                    """;

                var currentTransaction = _db.Database.CurrentTransaction;
                if (currentTransaction is not null)
                    existsCommand.Transaction = currentTransaction.GetDbTransaction();

                var hasPrimaryNodeConnections = await existsCommand.ExecuteScalarAsync() is not null;
                if (hasPrimaryNodeConnections)
                {
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM primary_node_connections WHERE user_id = {dbUser.Id}");
                }
            }
            finally
            {
                if (shouldCloseConnection)
                    await dbConnection.CloseAsync();
            }

            await _db.SaveChangesAsync();
            
            // Assign ownership of planets to the system
            var planets = _db.Planets.IgnoreQueryFilters().Where(x => x.OwnerId == dbUser.Id);
            foreach (var planet in planets)
            {
                planet.OwnerId = ISharedUser.VictorUserId;
                _db.Planets.Update(planet);
            }

            await _db.SaveChangesAsync();
            
            // Themes - reassign ownership to Victor (must happen before user deletion
            // since the FK would cascade delete them)
            var themes = _db.Themes.IgnoreQueryFilters().Where(x => x.AuthorId == dbUser.Id);
            foreach (var theme in themes)
            {
                theme.AuthorId = ISharedUser.VictorUserId;
                _db.Themes.Update(theme);
            }

            await _db.SaveChangesAsync();

            // Record a targeted deletion tombstone for every community node the
            // account joined BEFORE removing relationship rows. This is in the
            // same database transaction as deletion, so the node list cannot be
            // lost in a crash and unrelated nodes never learn the account id.
            var federationNodeDomains = await _db.FederatedMemberships
                .Where(x => x.UserId == dbUser.Id)
                .Select(x => x.NodeDomain)
                .Distinct()
                .ToListAsync();
            var outstandingInviteDomains = await _db.FederatedInviteGrants
                .Where(x => x.IntendedUserId == dbUser.Id)
                .Select(x => x.NodeDomain)
                .Distinct()
                .ToListAsync();
            federationNodeDomains = federationNodeDomains
                .Concat(outstandingInviteDomains)
                .Distinct()
                .ToList();
            if (Valour.Config.Configs.FederationConfig.Current?.HubEnabled == true)
            {
                var createdAt = DateTime.UtcNow;
                foreach (var nodeDomain in federationNodeDomains)
                {
                    _db.FederatedPurges.Add(new Valour.Database.FederatedPurge
                    {
                        Id = Valour.Server.Database.IdManager.Generate(),
                        SubjectUserId = dbUser.Id,
                        NodeDomain = nodeDomain,
                        CreatedAt = createdAt,
                    });
                }
            }

            // Remove the user's hub-side federation relationship records too, so a
            // deletion doesn't leave their accepted-domain and cross-node
            // membership data behind. (Empty/no-op on non-hub instances.)
            _db.FederatedAcceptedDomains.RemoveRange(
                _db.FederatedAcceptedDomains.Where(x => x.UserId == dbUser.Id));
            _db.FederatedMemberships.RemoveRange(
                _db.FederatedMemberships.Where(x => x.UserId == dbUser.Id));
            _db.FederatedInviteRedemptions.RemoveRange(
                _db.FederatedInviteRedemptions.Where(x => x.UserId == dbUser.Id));
            _db.FederatedInviteGrants.RemoveRange(
                _db.FederatedInviteGrants.Where(x => x.IntendedUserId == dbUser.Id || x.CreatorUserId == dbUser.Id));
            await _db.SaveChangesAsync();

            _db.Users.Remove(dbUser);
            await _db.SaveChangesAsync();

            await tran.CommitAsync();
            InvalidateAccessFlags(dbUser.Id);

            // Automod triggers and actions tied to the user's memberships were deleted
            if (planetMemberIds.Count > 0)
                AutomodService.InvalidateAllRulesCaches();

            // Evict after commit to avoid re-cache race
            foreach (var tokenId in revokedTokenIds)
            {
                _tokenService.RemoveFromQuickCache(tokenId);
                _coreHub.ForceLogoutToken(tokenId);
            }

            await DeleteStoredFilesAsync(dbUser.Id, deletedUploadHashes, ownedAppIds);

            _logger.LogInformation("Hard deleted user {UserName} ({UserId})", dbUser.Name, dbUser.Id);

            return TaskResult.SuccessResult;
        }
        catch(System.Exception e)
        {
            await tran.RollbackAsync();
            _db.ChangeTracker.Clear();
            _logger.LogError(e, "Error hard deleting user {UserName} ({UserId}). Base exception: {BaseExceptionMessage}",
                dbUser.Name, dbUser.Id, e.GetBaseException().Message);
            
            return new TaskResult(false, "An unexpected Database error occured.");
        }
    }

    /// <summary>
    /// Cancels the user's Stripe subscriptions immediately. Subscriptions
    /// bought through an app store are managed by that store and cannot be
    /// cancelled from here.
    /// </summary>
    private async Task<TaskResult> CancelStripeSubscriptionsAsync(long userId)
    {
        var stripeSubscriptionIds = await _db.UserSubscriptions.IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.Active && x.StripeSubscriptionId != null)
            .Select(x => x.StripeSubscriptionId)
            .ToListAsync();

        if (stripeSubscriptionIds.Count == 0)
            return TaskResult.SuccessResult;

        var stripeService = new Stripe.SubscriptionService();
        List<string> customerIds = [];
        foreach (var stripeSubscriptionId in stripeSubscriptionIds)
        {
            try
            {
                var subscription = await stripeService.GetAsync(stripeSubscriptionId);
                if (!string.IsNullOrEmpty(subscription.CustomerId))
                    customerIds.Add(subscription.CustomerId);

                if (subscription.Status is not ("canceled" or "incomplete_expired"))
                    await stripeService.CancelAsync(stripeSubscriptionId);
            }
            catch (Stripe.StripeException e) when (e.StripeError?.Code == "resource_missing")
            {
                // Already gone on Stripe's side; nothing left to bill.
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to cancel Stripe subscription {StripeSubscriptionId} while deleting user {UserId}",
                    stripeSubscriptionId, userId);
                return TaskResult.FromFailure(
                    "We couldn't cancel your subscription, so your account was not deleted. Try again, or contact support@valour.gg.");
            }
        }

        // Deleting the customer removes the saved payment methods and contact
        // details Stripe holds for them. Stripe keeps its own payment records.
        var customerService = new Stripe.CustomerService();
        foreach (var customerId in customerIds.Distinct())
        {
            try
            {
                await customerService.DeleteAsync(customerId);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to delete Stripe customer {CustomerId} while deleting user {UserId}",
                    customerId, userId);
            }
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Removes or detaches the user's content in planet features outside chat.
    /// Personal content (threads, comments, RSVPs, stats, folders) is deleted.
    /// Shared planet content that other members rely on (wiki pages, calendar
    /// events, webhooks) stays with the planet and is credited to the system user.
    /// </summary>
    private async Task DeleteCommunityContentAsync(long userId, List<long> planetMemberIds)
    {
        // Threads the user started are removed with every comment, attachment
        // and boost on them (those rows cascade from the thread).
        var authoredThreadIds = await _db.PlanetThreads.IgnoreQueryFilters()
            .Where(x => x.AuthorUserId == userId)
            .Select(x => x.Id)
            .ToListAsync();

        if (authoredThreadIds.Count > 0)
        {
            var removedCommentIds = await _db.ThreadComments.IgnoreQueryFilters()
                .Where(x => authoredThreadIds.Contains(x.ThreadId))
                .Select(x => x.Id)
                .ToListAsync();

            await _db.Planets.IgnoreQueryFilters()
                .Where(x => x.PinnedThreadId.HasValue && authoredThreadIds.Contains(x.PinnedThreadId.Value))
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.PinnedThreadId, (long?)null));

            await _db.PlanetReports.IgnoreQueryFilters()
                .Where(x => x.ThreadId.HasValue && authoredThreadIds.Contains(x.ThreadId.Value))
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.ThreadId, (long?)null));

            if (removedCommentIds.Count > 0)
            {
                await _db.PlanetReports.IgnoreQueryFilters()
                    .Where(x => x.ThreadCommentId.HasValue && removedCommentIds.Contains(x.ThreadCommentId.Value))
                    .ExecuteUpdateAsync(x => x.SetProperty(r => r.ThreadCommentId, (long?)null));
            }

            await _db.PlanetThreads.IgnoreQueryFilters()
                .Where(x => authoredThreadIds.Contains(x.Id))
                .ExecuteDeleteAsync();
        }

        // Comments on other people's threads become empty tombstones, the same
        // as a normal comment delete, so replies to them keep their place.
        await _db.ThreadComments.IgnoreQueryFilters()
            .Where(x => x.AuthorUserId == userId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(c => c.IsDeleted, true)
                .SetProperty(c => c.Content, string.Empty)
                .SetProperty(c => c.AuthorUserId, ISharedUser.VictorUserId)
                .SetProperty(c => c.AuthorMemberId, (long?)null));

        await _db.PlanetThreads.IgnoreQueryFilters()
            .Where(t => _db.ThreadBoosts.Any(b => b.ThreadId == t.Id && b.UserId == userId))
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.BoostCount, t => t.BoostCount - 1));
        await _db.ThreadBoosts.IgnoreQueryFilters()
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync();

        await _db.ThreadComments.IgnoreQueryFilters()
            .Where(c => _db.ThreadCommentBoosts.Any(b => b.CommentId == c.Id && b.UserId == userId))
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.BoostCount, c => c.BoostCount - 1));
        await _db.ThreadCommentBoosts.IgnoreQueryFilters()
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync();

        await _db.PlanetEventRsvps.IgnoreQueryFilters()
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync();

        await _db.PlanetEvents.IgnoreQueryFilters()
            .Where(x => x.AuthorUserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(e => e.AuthorUserId, ISharedUser.VictorUserId));

        await _db.PlanetWikiPages.IgnoreQueryFilters()
            .Where(x => x.CreatedByUserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.CreatedByUserId, ISharedUser.VictorUserId));
        await _db.PlanetWikiPages.IgnoreQueryFilters()
            .Where(x => x.LastEditedByUserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.LastEditedByUserId, (long?)ISharedUser.VictorUserId));
        await _db.PlanetWikiRevisions.IgnoreQueryFilters()
            .Where(x => x.AuthorUserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.AuthorUserId, ISharedUser.VictorUserId));

        await _db.PlanetWebhooks.IgnoreQueryFilters()
            .Where(x => x.CreatorUserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(w => w.CreatorUserId, ISharedUser.VictorUserId));

        await _db.PlanetReports.IgnoreQueryFilters()
            .Where(x => x.ReportingUserId == userId)
            .ExecuteDeleteAsync();
        await _db.PlanetReports.IgnoreQueryFilters()
            .Where(x => x.ReportedUserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.ReportedUserId, (long?)null));
        await _db.PlanetReports.IgnoreQueryFilters()
            .Where(x => x.ResolvedById == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.ResolvedById, (long?)null));

        await _db.UserActivityDays.IgnoreQueryFilters()
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync();
        await _db.UserPlanetFolders.IgnoreQueryFilters()
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync();
        await _db.UserPlanetSettings.IgnoreQueryFilters()
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync();

        await _db.VillageTemplates.IgnoreQueryFilters()
            .Where(x => x.PublishedByUserId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.PublishedByUserId, (long?)null));

        if (planetMemberIds.Count > 0)
        {
            await _db.PlanetReports.IgnoreQueryFilters()
                .Where(x => x.ReportedMemberId.HasValue && planetMemberIds.Contains(x.ReportedMemberId.Value))
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.ReportedMemberId, (long?)null));

            // Village plots, buildings and objects the user held return to the planet.
            await _db.VillagePlots.IgnoreQueryFilters()
                .Where(x => x.OwnerMemberId.HasValue && planetMemberIds.Contains(x.OwnerMemberId.Value))
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.OwnerMemberId, (long?)null));
            await _db.VillageBuildings.IgnoreQueryFilters()
                .Where(x => x.OwnerMemberId.HasValue && planetMemberIds.Contains(x.OwnerMemberId.Value))
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.OwnerMemberId, (long?)null));
            await _db.VillageObjects.IgnoreQueryFilters()
                .Where(x => x.OwnerMemberId.HasValue && planetMemberIds.Contains(x.OwnerMemberId.Value))
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.OwnerMemberId, (long?)null));
        }
    }

    /// <summary>
    /// Removes the deleted user's files from storage once the database no
    /// longer refers to them. Runs after commit; failures are logged, not
    /// surfaced, because the account itself is already gone.
    /// </summary>
    private async Task DeleteStoredFilesAsync(long userId, List<string> uploadHashes, List<long> appIds)
    {
        foreach (var hash in uploadHashes)
        {
            try
            {
                var result = await _bucketService.DeletePrivateObjectIfUnusedAsync(hash, _db);
                if (!result.Success)
                    _logger.LogWarning("Could not delete upload {Hash} of deleted user {UserId}: {Message}", hash, userId, result.Message);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not delete upload {Hash} of deleted user {UserId}", hash, userId);
            }
        }

        var publicPaths = UploadApi.GetPublicImagePaths("avatars", userId.ToString(), UploadApi.AvatarSizes)
            .Concat(UploadApi.GetPublicImagePaths("profiles", userId.ToString(), UploadApi.ProfileBackgroundSizes))
            .Concat(appIds.SelectMany(appId =>
                UploadApi.GetPublicImagePaths("apps", appId.ToString(), UploadApi.AppSizes)))
            .ToList();

        try
        {
            await _bucketService.DeletePublicObjectsAsync(publicPaths);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not delete public images of deleted user {UserId}", userId);
        }
    }

    public async Task<List<ReferralDataModel>> GetReferralDataAsync(long userId)
    {
        return await _db.Referrals.Include(x => x.User)
            .OrderByDescending(x => x.Created)
            .Where(x => x.ReferrerId == userId)
            .Select(x => new ReferralDataModel(){ Name = $"{x.User.Name}#{x.User.Tag}", Time = x.Created, Reward = x.Reward })
            .ToListAsync();
    }
    
    public async Task<bool> IsTagTaken(string username, string tag)
    {
        return await _db.Users.AnyAsync(x => x.Tag.ToLower() == tag.ToLower() && x.Name.ToLower() == username.ToLower());
    }
    
    public async Task<string> GetUniqueTag(string username)
    {
        var existing = await _db.Users.Where(x => x.Name.ToLower() == username.ToLower()).Select(x => x.Tag).ToListAsync();

        string tag;
        
        do
        {
            tag = GenerateRandomTag();
        } while (existing.Any(e => string.Equals(e, tag, StringComparison.OrdinalIgnoreCase)));

        return tag;
    }


    // TODO: Prevent the one in 1.6 million chance that you will get the tag F***, along with other 'bad words'
    // Just passed by this and realized the chances are far higher when accounting for similar-looking characters
    private string GenerateRandomTag()
    {
        return new string(Enumerable.Repeat(ISharedUser.TagChars, 4)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }
}
