namespace Valour.Config.Configs;

/// <summary>
/// First-run bootstrap settings for self-hosted instances. When an admin email
/// and password are provided and no staff account exists yet, a verified staff
/// account is created at startup.
/// </summary>
public class BootstrapConfig
{
    public static BootstrapConfig Current;

    public BootstrapConfig()
    {
        Current = this;
    }

    public string AdminEmail { get; set; }
    public string AdminPassword { get; set; }
    public string AdminUsername { get; set; } = "admin";
}
