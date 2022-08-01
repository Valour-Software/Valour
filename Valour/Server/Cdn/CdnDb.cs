using Valour.Server.Cdn.Objects;

namespace Valour.Server.Cdn;

public class CdnDb : DbContext
{
    public static string ConnectionString = $"Host={CdnConfig.Current.DbAddr};" +
                                            $"Database={CdnConfig.Current.DbName};" +
                                            $"Username={CdnConfig.Current.DbUser};" +
                                            $"Password={CdnConfig.Current.DbPass};" +
                                            $"SslMode=Prefer;";

    /// <summary>
    /// This is only here to fulfill the need of the constructor.
    /// It does literally nothing at all.
    /// </summary>
    public static DbContextOptions DBOptions;

    public DbSet<ProxyItem> ProxyItems { get; set; }
    public DbSet<BucketItem> BucketItems { get; set; }

    public CdnDb(DbContextOptions options)
    {

    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseNpgsql(ConnectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
