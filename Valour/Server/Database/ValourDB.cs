using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using System.Security.Claims;
using Valour.Server.Users;
using Valour.Server.Users.Identity;
using Valour.Shared.Oauth;
using Valour.Shared.Users;
using Valour.Shared.Planets;
using Valour.Server.Planets;
using Valour.Shared.Channels;
using Valour.Shared.Categories;
using Valour.Server.Email;
using Valour.Server.Messaging;
using Valour.Shared.Messages;

namespace Valour.Server.Database
{
    public class ValourDB : DbContext
    {

        public static string ConnectionString = $"server={DBConfig.instance.Host};port=3306;database={DBConfig.instance.Database};uid={DBConfig.instance.Username};pwd={DBConfig.instance.Password};SslMode=Required;";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseMySql(ConnectionString, ServerVersion.FromString("8.0.20-mysql"), options => options.EnableRetryOnFailure().CharSet(CharSet.Utf8Mb4));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }

        // These are the database sets we can access
        //public DbSet<ClientPlanetMessage> Messages { get; set; }

        /// <summary>
        /// This is only here to fulfill the need of the constructor.
        /// It does literally nothing at all.
        /// </summary>
        public static DbContextOptions DBOptions;

        /// <summary>
        /// Table for message cache
        /// </summary>
        public DbSet<Message> Messages { get; set; }

        /// <summary>
        /// Table for Valour users
        /// </summary>
        public DbSet<User> Users { get; set; }

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
        /// Table for all planet chat channels
        /// </summary>
        public DbSet<PlanetChatChannel> PlanetChatChannels { get; set; }

        /// <summary>
        /// Table for all planet chat categories
        /// </summary>
        public DbSet<PlanetCategory> PlanetCategories { get; set; }

        public ValourDB(DbContextOptions options)
        {
            
        }
    }
}
