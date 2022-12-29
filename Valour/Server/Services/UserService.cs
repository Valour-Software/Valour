using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Users;

namespace Valour.Server.Services;

public class UserService
{
    private readonly ValourDB _db;
    private readonly TokenService _tokenService;

    public UserService(ValourDB db, TokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Returns the user for the given id
    /// </summary>
    public async Task<User> GetAsync(long id) =>
        await _db.Users.FindAsync(id);


    /// <summary>
    /// Returns the auth token for the current context
    /// </summary>

    public async ValueTask<AuthToken> GetCurrentToken() =>
        await _tokenService.GetCurrentToken();
    
    /// <summary>
    /// Nuke it.
    /// </summary>
    public async Task HardDelete(ValourDB db)
    {
        var tran = await db.Database.BeginTransactionAsync();

        // Remove messages
        var pMsgs = db.PlanetMessages.Where(x => x.AuthorUserId == Id);
        db.PlanetMessages.RemoveRange(pMsgs);

        // Direct Message Channels
        var dChannels = await db.DirectChatChannels.Where(x => x.UserOneId == Id || x.UserTwoId == Id).ToListAsync();

        foreach (var dc in dChannels)
        {
            // channel states
            var st = db.UserChannelStates.Where(x => x.ChannelId == dc.Id);
            db.UserChannelStates.RemoveRange(st);

            // messages
            var dMsgs = db.DirectMessages.Where(x => x.ChannelId == dc.Id);
            db.DirectMessages.RemoveRange(dMsgs);

            await db.SaveChangesAsync();
        }

        db.DirectChatChannels.RemoveRange(dChannels);
        

        // Remove friends and friend requests
        var requests = db.UserFriends.Where(x => x.UserId == Id || x.FriendId == Id);
        db.UserFriends.RemoveRange(requests);

        // Remove email confirm codes
        var codes = db.EmailConfirmCodes.Where(x => x.UserId == Id);
        db.EmailConfirmCodes.RemoveRange(codes);


        // Remove user emails
        var emails = db.UserEmails.Where(x => x.UserId == Id);
        db.UserEmails.RemoveRange(emails);

        // Remove credentials
        var creds = db.Credentials.Where(x => x.UserId == Id);
        db.Credentials.RemoveRange(creds);

        var recovs = db.PasswordRecoveries.Where(x => x.UserId == Id);
        db.PasswordRecoveries.RemoveRange(recovs);

        // Remove membership stuff
        var pRoles = db.PlanetRoleMembers.Where(x => x.UserId == Id);
        db.PlanetRoleMembers.RemoveRange(pRoles);

        // Remove planet membership
        var members = db.PlanetMembers.Where(x => x.UserId == Id);
        db.PlanetMembers.RemoveRange(members);

        await db.SaveChangesAsync();

        // Authtokens
        var tokens = db.AuthTokens.Where(x => x.UserId == Id);
        db.AuthTokens.RemoveRange(tokens);

        // Referrals
        var refer = db.Referrals.Where(x => x.UserId == Id || x.ReferrerId == Id);
        db.Referrals.RemoveRange(refer);

        // Notifications
        var nots = db.NotificationSubscriptions.Where(x => x.UserId == Id);

        // Bans
        var bans = db.PlanetBans.Where(x => x.IssuerId == Id || x.TargetId == Id);
        db.PlanetBans.RemoveRange(bans);

        // Channel states
        var states = db.UserChannelStates.Where(x => x.UserId == Id);
        db.UserChannelStates.RemoveRange(states);

        // Planet invites
        var invites = db.PlanetInvites.Where(x => x.IssuerId == Id);
        db.PlanetInvites.RemoveRange(invites);

        await db.SaveChangesAsync();

        db.Users.Remove(this);
        await db.SaveChangesAsync();

        try
        {
            await tran.CommitAsync();
            Console.WriteLine("Deleting " + this.Name);
        }
        catch(System.Exception e)
        {
            Console.WriteLine("Error Hard Deleting User!");
            Console.WriteLine(e.Message);
        }
    }
}