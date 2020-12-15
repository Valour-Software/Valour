using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Valour.Server.Users;

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
        /// Table for Valour users
        /// </summary>
        public DbSet<User> Users { get; set; }

        /// <summary>
        /// Table for password and login information
        /// </summary>
        public DbSet<Credential> Credentials { get; set; }

        public ValourDB(DbContextOptions options)
        {

        }
    }
}
