using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Pomelo.EntityFrameworkCore.MySql.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Models;

namespace Valour.Server
{
    public class ValourDB : DbContext
    {

        public static string ConnectionString = $"server={DBConfig.instance.Host};port=3306;database=Valour;uid={DBConfig.instance.Username};pwd={DBConfig.instance.Password};SslMode=Required;";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseMySql(ConnectionString, options => options.EnableRetryOnFailure().CharSet(CharSet.Utf8Mb4).ServerVersion(new Version(8, 0, 20), ServerType.MySql));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }

        // These are the database sets we can access
        public DbSet<Message> Messages { get; set; }
        //public DbSet<User> Users { get; set; }
    }
}
