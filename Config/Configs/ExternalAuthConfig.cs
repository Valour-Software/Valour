namespace Valour.Config.Configs;

/// <summary>
/// OAuth clients for signing in with other services. A provider is offered
/// only when both its client ID and client secret are set.
/// </summary>
public class ExternalAuthConfig
{
    public static ExternalAuthConfig? Current;

    public ExternalAuthConfig()
    {
        Current = this;
    }

    public ExternalAuthProviderConfig? Google { get; set; }
    public ExternalAuthProviderConfig? Discord { get; set; }
}

public class ExternalAuthProviderConfig
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
