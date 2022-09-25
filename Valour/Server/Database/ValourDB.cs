using EntityFramework.Exceptions.PostgreSQL;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Channels;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Channels.Users;
using Valour.Server.Database.Items.Messages;
using Valour.Server.Database.Items.Notifications;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Server.Database.Nodes;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database;

public class ValourDB : DbContext
{

    public static ValourDB Instance = new ValourDB(DBOptions);

    public static string ConnectionString = $"Host={DBConfig.instance.Host};Database={DBConfig.instance.Database};Username={DBConfig.instance.Username};Password={DBConfig.instance.Password};SslMode=Prefer;";

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseNpgsql(ConnectionString).UseExceptionProcessor();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Composite key
        modelBuilder.Entity<UserChannelState>().HasKey(x => new { x.UserId, x.ChannelId });

        //base.OnModelCreating(modelBuilder);
    }

    // These are the database sets we can access
    //public DbSet<ClientPlanetMessage> Messages { get; set; }

    /// <summary>
    /// This is only here to fulfill the need of the constructor.
    /// It does literally nothing at all.
    /// </summary>
    public static DbContextOptions DBOptions = new DbContextOptionsBuilder().UseNpgsql(ConnectionString).UseExceptionProcessor().Options;

    /// <summary>
    /// Table for message cache
    /// </summary>
    public DbSet<PlanetMessage> PlanetMessages { get; set; }

    /// <summary>
    /// Table for Valour users
    /// </summary>
    public DbSet<User> Users { get; set; }

    /// <summary>
    /// Table for Valour user friends
    /// </summary>
    public DbSet<UserFriend> UserFriends { get; set; }

    // USER LOGIN AND PERMISSION STUFF //

    /// <summary>
    /// Table for password and login information
    /// </summary>
    public DbSet<Credential> Credentials { get; set; }

    /// <summary>
    /// Table for email information
    /// </summary>
    public DbSet<UserEmail> UserEmails { get; set; }

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
    /// Table for all direct chat channels
    /// </summary>
    public DbSet<DirectChatChannel> DirectChatChannels { get; set; }

    /// <summary>
    /// Table for all planet channels
    /// </summary>
    public DbSet<PlanetChannel> PlanetChannels { get; set; }

    /// <summary>
    /// Table for all planet chat channels
    /// </summary>
    public DbSet<PlanetChatChannel> PlanetChatChannels { get; set; }

    /// <summary>
    /// Table for all planet chat categories
    /// </summary>
    public DbSet<PlanetCategoryChannel> PlanetCategoryChannels { get; set; }

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

    public DbSet<NodeStats> NodeStats { get; set; }

    public DbSet<PrimaryNodeConnection> PrimaryNodeConnections { get; set; }

    public ValourDB(DbContextOptions options)
    {

    }
}

