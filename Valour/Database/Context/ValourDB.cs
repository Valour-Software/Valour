using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Valour.Database.Config;
using Valour.Database.Economy;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Context;

public class ValourDB : DbContext
{
    public static readonly string ConnectionString = $"Host={DbConfig.Instance.Host};Database={DbConfig.Instance.Database};Username={DbConfig.Instance.Username};Password={DbConfig.Instance.Password};SslMode=Prefer;";

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.ConfigureWarnings(w => w.Ignore(RelationalEventId.ForeignKeyPropertiesMappedToUnrelatedTables));
        options.UseNpgsql(ConnectionString).UseExceptionProcessor();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Composite key
        modelBuilder.Entity<UserChannelState>().HasKey(x => new { x.UserId, x.ChannelId });

        // Soft delete
        modelBuilder.Entity<Planet>().HasQueryFilter(x => x.IsDeleted == false);
        modelBuilder.Entity<PlanetMember>().HasQueryFilter(x => x.IsDeleted == false);
        modelBuilder.Entity<Channel>().HasQueryFilter(x => x.IsDeleted == false);
        
        // can only add query filters to root entities
        // modelBuilder.Entity<DirectChatChannel>().HasQueryFilter(x => x.IsDeleted == false);
        // modelBuilder.Entity<PlanetChannel>().HasQueryFilter(x => x.IsDeleted == false);
        // modelBuilder.Entity<PlanetChatChannel>().HasQueryFilter(x => x.IsDeleted == false);
        // modelBuilder.Entity<PlanetCategory>().HasQueryFilter(x => x.IsDeleted == false);
        // modelBuilder.Entity<PlanetVoiceChannel>().HasQueryFilter(x => x.IsDeleted == false); 
        
        //base.OnModelCreating(modelBuilder);
    }

    // These are the database sets we can access
    //public DbSet<ClientPlanetMessage> Messages { get; set; }
    
    /// <summary>
    /// Table for messages
    /// </summary>
    public DbSet<Message> Messages { get; set; }
    
    /// <summary>
    /// Table for Valour users
    /// </summary>
    public DbSet<User> Users { get; set; }
    
    /// <summary>
    /// Table for user subscriptions
    /// </summary>
    public DbSet<UserSubscription> UserSubscriptions { get; set; }

    /// <summary>
    /// Table for Valour user profiles
    /// </summary>
    public DbSet<UserProfile> UserProfiles { get; set; }

    /// <summary>
    /// Table for Valour user friends
    /// </summary>
    public DbSet<UserFriend> UserFriends { get; set; }
    
    /// <summary>
    /// Table for user Tenor favorites
    /// </summary>
    public DbSet<TenorFavorite> TenorFavorites { get; set; }

    // USER LOGIN AND PERMISSION STUFF //

    /// <summary>
    /// Table for password and login information
    /// </summary>
    public DbSet<Credential> Credentials { get; set; }

    /// <summary>
    /// Table for email information
    /// </summary>
    public DbSet<UserPrivateInfo> UserEmails { get; set; }

    /// <summary>
    /// Table for blocked email addresses and hosts
    /// </summary>
    public DbSet<BlockedUserEmail> BlockedUserEmails { get; set; }

    /// <summary>
    /// Table for authentication tokens
    /// </summary>
    public DbSet<AuthToken> AuthTokens { get; set; }

    /// <summary>
    /// Table for email confirmation codes
    /// </summary>
    public DbSet<EmailConfirmCode> EmailConfirmCodes { get; set; }

    /// <summary>
    /// Table for planet definitions
    /// </summary>
    public DbSet<Planet> Planets { get; set; }

    /// <summary>
    /// Table for all planet membership
    /// </summary>
    public DbSet<PlanetMember> PlanetMembers { get; set; }

    /// <summary>
    /// Table for all channels
    /// </summary>
    public DbSet<Channel> Channels { get; set; }

    /// <summary>
    /// Table for all banned members
    /// </summary>
    public DbSet<PlanetBan> PlanetBans { get; set; }

    /// <summary>
    /// Table for planet invites
    /// </summary>
    public DbSet<PlanetInvite> PlanetInvites { get; set; }

    /// <summary>
    /// Table for planet invites
    /// </summary>
    public DbSet<StatObject> Stats { get; set; }

    /// <summary>
    /// Table for referrals
    /// </summary>
    public DbSet<Referral> Referrals { get; set; }

    /// <summary>
    /// Table for recoveries
    /// </summary>
    public DbSet<PasswordRecovery> PasswordRecoveries { get; set; }

    /// <summary>
    /// Table for notification subscriptions
    /// </summary>
    public DbSet<NotificationSubscription> NotificationSubscriptions { get; set; }
    
    /// <summary>
    /// Table for notifications
    /// </summary>
    public DbSet<Notification> Notifications { get; set; }

    /// <summary>
    /// Table for members of planet roles
    /// </summary>
    public DbSet<PlanetRoleMember> PlanetRoleMembers { get; set; }

    /// <summary>
    /// Table for Oauth apps
    /// </summary>
    public DbSet<OauthApp> OauthApps { get; set; }

    public DbSet<PermissionsNode> PermissionsNodes { get; set; }

    public DbSet<PlanetRole> PlanetRoles { get; set; }

    public DbSet<UserChannelState> UserChannelStates { get; set; }
    
    public DbSet<ChannelState> ChannelStates { get; set; }

    public DbSet<NodeStats> NodeStats { get; set; }
    
    public DbSet<Report> Reports { get; set; }

    ////////////////
    // Eco System //
    ////////////////

    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<Currency> Currencies { get; set; }
    public DbSet<EcoAccount> EcoAccounts { get; set; }

    public ValourDB(DbContextOptions options)
    {

    }
}

