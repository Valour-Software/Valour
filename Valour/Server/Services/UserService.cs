using Valour.Server.Email;
using Valour.Server.Users;
using Valour.Server.Utilities;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class UserService
{
    private readonly ValourDB _db;
    private readonly TokenService _tokenService;
    private readonly ILogger<UserService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly NodeService _nodeService;


    /// <summary>
    /// The stored user for the current request
    /// </summary>
    private User _currentUser;

    public UserService(
        ValourDB db,
        TokenService tokenService,
        ILogger<UserService> logger,
        CoreHubService coreHub,
        NodeService nodeService)
    {
        _db = db;
        _tokenService = tokenService;
        _logger = logger;
        _coreHub = coreHub;
        _nodeService = nodeService;
    }

    public Task<int> GetUserCountAsync()
        => _db.Users.CountAsync();

    public async Task<IEnumerable<string>> GetNewUsersAsync(int count)
    {
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

    /// <summary>
    /// Queries users by the given attributes and returns the results
    /// </summary>
    /// <param name="usernameAndTag">The username + tag search</param>
    /// <param name="skip">The number of results to skip</param>
    /// <param name="take">The number of results to return</param>
    /// <returns></returns>
    public async Task<PagedResponse<User>> QueryUsersAsync(string usernameAndTag, int skip = 0, int take = 50)
    {
        if (take > 50)
        {
            take = 50;
        }

        var query = _db.Users
            .Where(x => EF.Functions.ILike((x.Name.ToLower() + "#" + x.Tag), "%" + usernameAndTag.ToLower() + "%"))
            .OrderBy(x => x.Name);

        var totalCount = await query.CountAsync();

        var users = await query.Take(take)
            .Select(x => x.ToModel())
            .ToListAsync();

        return new PagedResponse<User>()
        {
            TotalCount = totalCount,
            Items = users
        };
    }

    public async Task<EmailConfirmCode> GetEmailConfirmCode(string code) =>
        (await _db.EmailConfirmCodes.FirstOrDefaultAsync(x => x.Code == code)).ToModel();

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
    
    public async Task<List<Planet>> GetPlanetsUserIn(long userId)
    {
        var planets = await _db.PlanetMembers
            .Where(x => x.UserId == userId)
            .Include(x => x.Planet)
            .Select(x => x.Planet.ToModel())
            .ToListAsync();

        foreach (var planet in planets)
        {
            planet.NodeName = await _nodeService.GetPlanetNodeAsync(planet.Id);
        }

        return planets;
    }

    public async Task<PasswordRecovery> GetPasswordRecoveryAsync(string code) =>
        (await _db.PasswordRecoveries.FirstOrDefaultAsync(x => x.Code == code)).ToModel();

    public async Task<Valour.Database.Credential> GetCredentialAsync(long userId) =>
        await _db.Credentials.FirstOrDefaultAsync(x => x.UserId == userId);

    public async Task<List<UserChannelState>> GetUserChannelStatesAsync(long userId) =>
        await _db.UserChannelStates.Where(x => x.UserId == userId).Select(x => x.ToModel()).ToListAsync();

    public async Task<List<TenorFavorite>> GetTenorFavoritesAsync(long userId) =>
        await _db.TenorFavorites.Where(x => x.UserId == userId).Select(x => x.ToModel()).ToListAsync();

    public async Task<(List<User> added, List<User> addedBy)> GetFriendsDataAsync(long userId)
    {
        // Users added by this user as a friend (user -> other)
        var added = await _db.UserFriends.Include(x => x.Friend).Where(x => x.UserId == userId).Select(x => x.Friend.ToModel()).ToListAsync();

        // Users who added this user as a friend (other -> user)
        var addedBy = await _db.UserFriends.Include(x => x.User).Where(x => x.FriendId == userId).Select(x => x.User.ToModel()).ToListAsync();

        return (added, addedBy);
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

    public async Task<UserPrivateInfo> GetUserEmailAsync(string email, bool makelowercase = true)
    {
        if (!makelowercase)
            return (await _db.UserEmails.FindAsync(email)).ToModel();
        else
            return (await _db.UserEmails.FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower())).ToModel();
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

            PasswordRecovery recovery = new()
            {
                Code = recoveryCode,
                UserId = userPrivateInfo.UserId
            };

            await _db.PasswordRecoveries.AddAsync(recovery.ToDatabase());
            await _db.SaveChangesAsync();

            var host = ctx.Request.Host.ToUriComponent();
            string link = $"{ctx.Request.Scheme}://{host}/RecoverPassword/{recoveryCode}";

            string emsg = $@"<body style='font-family: Ubuntu, Arial, sans-serif; margin: 0; padding: 0; background-color: #f4f4f4;'>
            <div style='max-width: 600px; margin: 20px auto; background-color: #fff; padding: 20px; border-radius: 5px; box-shadow: 0 0 10px rgba(0, 0, 0, 0.1);'>
            <img src='https://valour.gg/media/logo/logo-64.png' alt='Valour Logo' style='max-width: 100%; height: auto; display: block; margin: 0 auto;'>
            <h1 style='color: #333;'>Password Reset</h1>
            <p style='color: #666;'>Hello,</p>
            <p style='color: #666;'>You have requested a password reset for your account. To reset your password, please click the button below:</p>
            <a href='{link}' style='display: inline-block; padding: 10px 20px; background-color: #3498db; color: #fff; text-decoration: none; border-radius: 3px;'>Reset Password</a>
            <p style='color: #666;'>If you are unable to click the button, you can also copy and paste the following link into your browser:</p>
            <p style='color: #666;'><a href='{link}'>{link}</a></p>
            <p style='color: #666;'>Thank you,<br>Valour Team</p>
            </div>
            </body>";

            string rawmsg = $"To reset your password, please go to the following link:\n{link}";

            var result = await EmailManager.SendEmailAsync(email, "Valour Password Recovery", rawmsg, emsg);

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

    public async Task<TaskResult> RecoveryUserAsync(PasswordRecoveryRequest request, PasswordRecovery recovery, Valour.Database.Credential cred)
    {
        using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.PasswordRecoveries.Remove(await _db.PasswordRecoveries.FindAsync(recovery.Code));

            byte[] salt = PasswordManager.GenerateSalt();
            byte[] hash = PasswordManager.GetHashForPassword(request.Password, salt);

            cred.Salt = salt;
            cred.Secret = hash;

            _db.Credentials.Update(cred);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, "We're sorry. Something unexpected occured. Try again?");
        }

        await tran.CommitAsync();

        return new(true, "Success");
    }

    public int GetYearsOld(DateTime birthDate)
    {
        var now = DateTime.Today;
        var age = now.Year - birthDate.Year;
        if (birthDate > now.AddYears(-age)) age--;

        return age;
    }

    public async Task<TaskResult> SetUserComplianceData(long userId, DateTime birthDate, Locality locality)
    {
        if (GetYearsOld(birthDate) < 13)
            return new TaskResult(false, "You must be 13 or older to use Valour. Sorry!");

        birthDate = DateTime.SpecifyKind(birthDate, DateTimeKind.Utc);
        
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return new TaskResult(false, "User not found");
        
        var userPrivateInfo = await _db.UserEmails.FirstOrDefaultAsync(x => x.UserId == userId);
        if (userPrivateInfo is null)
            return new TaskResult(false, "User info not found");

        await using var trans = await _db.Database.BeginTransactionAsync();

        try
        {
            userPrivateInfo.BirthDate = birthDate;
            userPrivateInfo.Locality = locality;

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
        => (await _db.Users.FirstOrDefaultAsync(x => x.Name.ToLower() == username.ToLower() && x.Tag == tag.ToUpper())).ToModel();

    
    /// <summary>
    /// Returns a user given the full name and tag: SpikeViper#0000
    /// </summary>
    /// <param name="username"></param>
    /// <returns></returns>
    public async Task<User> GetByNameAsync(string username)
    {
        var split = username.Split('#');
        if (split.Length < 2)
        {
            return null;
        }
        
        // Users are searched by lowercase name, but the tags are uppercase
        return await GetUserAsync(split[0], split[1]);
    }
    
    public async Task<TaskResult<User>> UpdateAsync(User updatedUser)
    {
        var old = await _db.Users.FindAsync(updatedUser.Id); 
        if (old is null)
            return new TaskResult<User>(false, "Could not find user");

        old.Status = updatedUser.Status;
        old.UserStateCode = updatedUser.UserStateCode;

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

        await _coreHub.NotifyUserChange(old.ToModel());

        return new(true, "Success", old.ToModel());
    }

    public async Task<TaskResult> VerifyAsync(string code)
    {
        await using var tran = await _db.Database.BeginTransactionAsync();
        var confirmCode = await _db.EmailConfirmCodes.FirstOrDefaultAsync(x => x.Code == code);
        if (confirmCode is null)
            return new TaskResult(false, "Code not found.");
        
        try
        {
            var email = await _db.UserEmails.FirstOrDefaultAsync(x => x.UserId == confirmCode.UserId);
            email.Verified = true;
            
            _db.EmailConfirmCodes.Remove(confirmCode);
            await _db.SaveChangesAsync();
            
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

    public async Task<TaskResult> Logout()
    {
        try
        {
            var key = _tokenService.GetAuthKey();
            var dbToken = await _db.AuthTokens.FindAsync(key);
            _db.AuthTokens.Remove(dbToken);
            _tokenService.RemoveFromQuickCache(key);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success");
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

    public async Task<TaskResult<User>> ValidateAsync(string credential_type, string identifier, string secret)
    {
        // Find the credential that matches the identifier and type
        Valour.Database.Credential credential = await _db.Credentials.FirstOrDefaultAsync(
            x => string.Equals(credential_type.ToUpper(), x.CredentialType.ToUpper()) &&
                    string.Equals(identifier.ToUpper(), x.Identifier.ToUpper()));

        if (credential == null || string.IsNullOrWhiteSpace(secret))
        {
            return new TaskResult<User>(false, "The credentials were incorrect.", null);
        }

        // Use salt to validate secret hash
        byte[] hash = PasswordManager.GetHashForPassword(secret, credential.Salt);

        // Spike needs to remember how reference types work 
        if (!hash.SequenceEqual(credential.Secret))
        {
            return new TaskResult<User>(false, "The credentials were incorrect.", null);
        }

        User user = await GetAsync(credential.UserId);

        if (user.Disabled)
        {
            return new TaskResult<User>(false, "This account has been disabled", null);
        }

        return new TaskResult<User>(true, "Succeeded", user);
    }

    public async Task<TaskResult<AuthToken>> GetTokenAfterLoginAsync(HttpContext ctx, long userId)
    {
        // Check for an old token
        var token = await _db.AuthTokens
            .FirstOrDefaultAsync(x => x.AppId == "VALOUR" &&
                                      x.UserId == userId &&
                                      x.Scope == UserPermissions.FullControl.Value);

        try
        {
            if (token is null)
            {
                // We now have to create a token for the user
                token = new AuthToken()
                {
                    AppId = "VALOUR",
                    Id = "val-" + Guid.NewGuid().ToString(),
                    TimeCreated = DateTime.UtcNow,
                    TimeExpires = DateTime.UtcNow.AddDays(7),
                    Scope = UserPermissions.FullControl.Value,
                    UserId = userId,
                    IssuedAddress = ctx.Connection.RemoteIpAddress.ToString()
                }.ToDatabase();

                await _db.AuthTokens.AddAsync(token);
                await _db.SaveChangesAsync();
            }
            else
            {
                token.TimeCreated = DateTime.UtcNow;
                token.TimeExpires = DateTime.UtcNow.AddDays(7);

                _db.Entry(token).State = EntityState.Detached;
                _db.AuthTokens.Update(token);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        return new(true, "Success", token.ToModel());
    }

    /// <summary>
    /// Nuke it.
    /// </summary>
    public async Task<TaskResult> HardDelete(User user)
    {
        var tran = await _db.Database.BeginTransactionAsync();
        
        var dbUser = await _db.Users.FindAsync(user.Id);
        if (dbUser is null)
            return TaskResult.FromError("User not found.");
        
        try
        {
            // Remove messages
            await _db.Messages.IgnoreQueryFilters().Where(x => x.AuthorUserId == dbUser.Id)
                .ExecuteDeleteAsync();
            
            // Remove message attachments
            var msgAttachments = _db.CdnBucketItems.IgnoreQueryFilters().Where(x => x.UserId == user.Id);
            _db.CdnBucketItems.RemoveRange(msgAttachments);
            
            await _db.SaveChangesAsync();

            // Channel states
            var states = _db.UserChannelStates.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.UserChannelStates.RemoveRange(states);

            await _db.SaveChangesAsync();

            var memberIds = await _db.PlanetMembers.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id).Select(x => x.Id).ToListAsync();
            foreach (var memberId in memberIds)
            {
                // Channel access
                var access = _db.MemberChannelAccess.IgnoreQueryFilters().Where(x => x.MemberId == memberId);
                _db.MemberChannelAccess.RemoveRange(access);
            }
            
            await _db.SaveChangesAsync();
            
            // Channel membership
            var dchannelMembers = _db.ChannelMembers.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.ChannelMembers.RemoveRange(dchannelMembers);

            await _db.SaveChangesAsync();
            
            // Direct Message Channels
            var dChannels = await _db.Channels
                .IgnoreQueryFilters()
                .Include(x => x.Members)
                .Where(x => x.ChannelType == ChannelTypeEnum.DirectChat && 
                                    x.Members.Any(m => m.UserId == dbUser.Id))
                .ToListAsync();

            foreach (var dc in dChannels)
            {
                // channel states
                var st = _db.UserChannelStates.IgnoreQueryFilters().Where(x => x.ChannelId == dc.Id);
                _db.UserChannelStates.RemoveRange(st);
                
                var pst = _db.ChannelStates.IgnoreQueryFilters().Where(x => x.ChannelId == dc.Id);
                _db.ChannelStates.RemoveRange(pst);
                
                // notifications
                var dnots = _db.Notifications.IgnoreQueryFilters().Where(x => x.ChannelId == dc.Id);
                _db.Notifications.RemoveRange(dnots);
                
                await _db.SaveChangesAsync();
            }
            
            

            _db.Channels.RemoveRange(dChannels);
            
            await _db.SaveChangesAsync();

            // Remove friends and friend requests
            var requests = _db.UserFriends.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id || x.FriendId == dbUser.Id);
            _db.UserFriends.RemoveRange(requests);

            // Remove email confirm codes
            var codes = _db.EmailConfirmCodes.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.EmailConfirmCodes.RemoveRange(codes);
            
            // Remove user emails
            var emails = _db.UserEmails.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.UserEmails.RemoveRange(emails);

            // Remove credentials
            var creds = _db.Credentials.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.Credentials.RemoveRange(creds);

            var recovs = _db.PasswordRecoveries.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.PasswordRecoveries.RemoveRange(recovs);
            
            await _db.SaveChangesAsync();
            
            // Remove eco stuff
            var transactions = _db.Transactions.IgnoreQueryFilters().Where(x => x.UserFromId == dbUser.Id || x.UserToId == dbUser.Id);
            _db.Transactions.RemoveRange(transactions);
            
            var ecoAccounts = _db.EcoAccounts.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.EcoAccounts.RemoveRange(ecoAccounts);
            
            await _db.SaveChangesAsync();

            // Remove membership stuff
            var pRoles = _db.PlanetRoleMembers.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.PlanetRoleMembers.RemoveRange(pRoles);

            // Remove planet membership
            var members = _db.PlanetMembers.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.PlanetMembers.RemoveRange(members);

            await _db.SaveChangesAsync();

            // Authtokens
            var tokens = _db.AuthTokens.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.AuthTokens.RemoveRange(tokens);

            // Referrals
            var refer = _db.Referrals.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id || x.ReferrerId == dbUser.Id);
            _db.Referrals.RemoveRange(refer);

            // Notifications
            var nots = _db.NotificationSubscriptions.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.NotificationSubscriptions.RemoveRange(nots);
            
            // Also notifications
            var noots  = _db.Notifications.IgnoreQueryFilters().Where(x => x.UserId == dbUser.Id);
            _db.Notifications.RemoveRange(noots);
            
            // Bans
            var bans = _db.PlanetBans.IgnoreQueryFilters().Where(x => x.IssuerId == dbUser.Id || x.TargetId == dbUser.Id);
            _db.PlanetBans.RemoveRange(bans);

            // Planet invites
            var invites = _db.PlanetInvites.IgnoreQueryFilters().Where(x => x.IssuerId == dbUser.Id);
            _db.PlanetInvites.RemoveRange(invites);

            await _db.SaveChangesAsync();
            
            // Assign ownership of planets to the system
            var planets = _db.Planets.IgnoreQueryFilters().Where(x => x.OwnerId == dbUser.Id);
            foreach (var planet in planets)
            {
                planet.OwnerId = ISharedUser.VictorUserId;
                _db.Planets.Update(planet);
            }

            await _db.SaveChangesAsync();
            
            // profile
            var profile = await _db.UserProfiles.FindAsync(dbUser.Id);
            if (profile is not null)
            {
                _db.UserProfiles.Remove(profile);
                await _db.SaveChangesAsync();
            }

            _db.Users.Remove(dbUser);
            await _db.SaveChangesAsync();
            
            // Themes
            // we re-assign ownership of themes to Victor
            
            var themes = _db.Themes.IgnoreQueryFilters().Where(x => x.AuthorId == dbUser.Id);
            foreach (var theme in themes)
            {
                theme.AuthorId = ISharedUser.VictorUserId;
                _db.Themes.Update(theme);
            }
            
            await _db.SaveChangesAsync();
        
            await tran.CommitAsync();
            Console.WriteLine("Deleting " + dbUser.Name);

            return TaskResult.SuccessResult;
        }
        catch(System.Exception e)
        {
            await tran.RollbackAsync();
            Console.WriteLine("Error Hard Deleting User!");
            Console.WriteLine(e.Message);
            
            return new TaskResult(false, "An unexpected Database error occured.");
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
        return await _db.Users.AnyAsync(x => x.Tag == tag && x.Name.ToLower() == username.ToLower());
    }
    
    public async Task<string> GetUniqueTag(string username)
    {
        var existing = await _db.Users.Where(x => x.Name.ToLower() == username.ToLower()).Select(x => x.Tag).ToListAsync();

        string tag;
        
        do
        {
            tag = GenerateRandomTag();
        } while (existing.Contains(tag));

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