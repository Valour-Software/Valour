using Microsoft.EntityFrameworkCore;
using Valour.Server.Users;
using Valour.Server.Users.Identity;
using Valour.Shared.Users;
using Valour.Shared.Planets;
using Valour.Server.Planets;
using Valour.Server.Email;
using Valour.Shared.Messages;
using Valour.Server.Roles;
using Valour.Server.Oauth;
using Valour.Server.Categories;
using Valour.Shared.Notifications;

namespace Valour.Server.Database
{
    public class ValourDB : DbContext
    {

        public static string ConnectionString = $"server={DBConfig.instance.Host};port=3306;database={DBConfig.instance.Database};uid={DBConfig.instance.Username};pwd={DBConfig.instance.Password};SslMode=Required;charset=utf8mb4;";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseMySql(ConnectionString, ServerVersion.Parse("8.0.20-mysql"), options => { 
                options.EnableRetryOnFailure(); 
            });
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            //modelBuilder.HasCharSet(CharSet.Utf8Mb4);
        }

        // These are the database sets we can access
        //public DbSet<ClientPlanetMessage> Messages { get; set; }

        /// <summary>
        /// This is only here to fulfill the need of the constructor.
        /// It does literally nothing at all.
        /// </summary>
        public static DbContextOptions DBOptions = new DbContextOptionsBuilder().UseMySql(ConnectionString, ServerVersion.Parse("8.0.20-mysql"), options => {
            options.EnableRetryOnFailure();
        }).Options;

        /// <summary>
        /// Table for message cache
        /// </summary>
        public DbSet<PlanetMessage> PlanetMessages { get; set; }

        /// <summary>
        /// Table for Valour users
        /// </summary>
        public DbSet<ServerUser> Users { get; set; }

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
        public DbSet<ServerAuthToken> AuthTokens { get; set; }

        /// <summary>
        /// Table for email confirmation codes
        /// </summary>
        public DbSet<EmailConfirmCode> EmailConfirmCodes { get; set; }

        /// <summary>
        /// Table for planet definitions
        /// </summary>
        public DbSet<ServerPlanet> Planets { get; set; }

        /// <summary>
        /// Table for all planet membership
        /// </summary>
        public DbSet<ServerPlanetMember> PlanetMembers { get; set; }

        /// <summary>
        /// Table for all planet chat channels
        /// </summary>
        public DbSet<ServerPlanetChatChannel> PlanetChatChannels { get; set; }

        /// <summary>
        /// Table for all planet chat categories
        /// </summary>
        public DbSet<ServerPlanetCategory> PlanetCategories { get; set; }

        /// <summary>
        /// Table for all banned members
        /// </summary>
        public DbSet<PlanetBan> PlanetBans { get; set; }

        /// <summary>
        /// Table for planet invites
        /// </summary>
        public DbSet<ServerPlanetInvite> PlanetInvites { get; set; }

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
        public DbSet<ServerPlanetRoleMember> PlanetRoleMembers { get; set; }

        public DbSet<PermissionsNode> PermissionsNodes { get; set; }

        public DbSet<ServerPlanetRole> PlanetRoles { get; set; }

        public ValourDB(DbContextOptions options)
        {

        }
    }
}
