using IdGen;
using Valour.Database.Context;

namespace Valour.Server.Services;

public class UserService
{
    private readonly ValourDB _db;
    private readonly TokenService _tokenService;

    /// <summary>
    /// The stored user for the current request
    /// </summary>
    private User _currentUser;

    public UserService(ValourDB db, TokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Returns the user for the given id
    /// </summary>
    public async Task<User> GetAsync(long id) =>
        (await _db.Users.FindAsync(id)).ToModel();


    /// <summary>
    /// Returns the user for the current context
    /// </summary>

    public async Task<User> GetCurrentUser()
    {
        var token = await _tokenService.GetCurrentToken();
        _currentUser = await GetAsync(token.UserId);
        return _currentUser;
    }
    
    /// <summary>
    /// Returns the user id for the current context
    /// </summary>

    public async Task<long?> GetCurrentUserId()
    {
        var token = await _tokenService.GetCurrentToken();
        return token?.UserId;
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

    /// <summary>
    /// Nuke it.
    /// </summary>
    public async Task HardDelete(User user)
    {
        var tran = await _db.Database.BeginTransactionAsync();
        
        var dbUser = await _db.Users.FindAsync(user.Id);
        if (dbUser is null)
            return;

        // Remove messages
        var pMsgs = _db.PlanetMessages.Where(x => x.AuthorUserId == dbUser.Id);
        _db.PlanetMessages.RemoveRange(pMsgs);

        // Direct Message Channels
        var dChannels = await _db.DirectChatChannels
            .Where(x => x.UserOneId == dbUser.Id || x.UserTwoId == dbUser.Id)
            .ToListAsync();

        foreach (var dc in dChannels)
        {
            // channel states
            var st = _db.UserChannelStates.Where(x => x.ChannelId == dc.Id);
            _db.UserChannelStates.RemoveRange(st);

            // messages
            var dMsgs = _db.DirectMessages.Where(x => x.ChannelId == dc.Id);
            _db.DirectMessages.RemoveRange(dMsgs);

            await _db.SaveChangesAsync();
        }

        _db.DirectChatChannels.RemoveRange(dChannels);
        

        // Remove friends and friend requests
        var requests = _db.UserFriends.Where(x => x.UserId == dbUser.Id || x.FriendId == dbUser.Id);
        _db.UserFriends.RemoveRange(requests);

        // Remove email confirm codes
        var codes = _db.EmailConfirmCodes.Where(x => x.UserId == dbUser.Id);
        _db.EmailConfirmCodes.RemoveRange(codes);


        // Remove user emails
        var emails = _db.UserEmails.Where(x => x.UserId == dbUser.Id);
        _db.UserEmails.RemoveRange(emails);

        // Remove credentials
        var creds = _db.Credentials.Where(x => x.UserId == dbUser.Id);
        _db.Credentials.RemoveRange(creds);

        var recovs = _db.PasswordRecoveries.Where(x => x.UserId == dbUser.Id);
        _db.PasswordRecoveries.RemoveRange(recovs);

        // Remove membership stuff
        var pRoles = _db.PlanetRoleMembers.Where(x => x.UserId == dbUser.Id);
        _db.PlanetRoleMembers.RemoveRange(pRoles);

        // Remove planet membership
        var members = _db.PlanetMembers.Where(x => x.UserId == dbUser.Id);
        _db.PlanetMembers.RemoveRange(members);

        await _db.SaveChangesAsync();

        // Authtokens
        var tokens = _db.AuthTokens.Where(x => x.UserId == dbUser.Id);
        _db.AuthTokens.RemoveRange(tokens);

        // Referrals
        var refer = _db.Referrals.Where(x => x.UserId == dbUser.Id || x.ReferrerId == dbUser.Id);
        _db.Referrals.RemoveRange(refer);

        // Notifications
        var nots = _db.NotificationSubscriptions.Where(x => x.UserId == dbUser.Id);

        // Bans
        var bans = _db.PlanetBans.Where(x => x.IssuerId == dbUser.Id || x.TargetId == dbUser.Id);
        _db.PlanetBans.RemoveRange(bans);

        // Channel states
        var states = _db.UserChannelStates.Where(x => x.UserId == dbUser.Id);
        _db.UserChannelStates.RemoveRange(states);

        // Planet invites
        var invites = _db.PlanetInvites.Where(x => x.IssuerId == dbUser.Id);
        _db.PlanetInvites.RemoveRange(invites);

        await _db.SaveChangesAsync();

        _db.Users.Remove(dbUser);
        await _db.SaveChangesAsync();

        try
        {
            await tran.CommitAsync();
            Console.WriteLine("Deleting " + dbUser.Name);
        }
        catch(System.Exception e)
        {
            Console.WriteLine("Error Hard Deleting User!");
            Console.WriteLine(e.Message);
        }
    }
}