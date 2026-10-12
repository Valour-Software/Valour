namespace Valour.Config.Configs;

/// <summary>
/// OAuth clients for signing in with other services. A provider is offered
/// only when its settings are complete: a client ID and client secret for
/// Google and Discord, and the fields of <see cref="AppleAuthProviderConfig"/>
/// for Apple.
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
    public AppleAuthProviderConfig? Apple { get; set; }
}

public class ExternalAuthProviderConfig
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public virtual bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Sign in with Apple. Apple has no fixed client secret: the server signs a
/// short-lived token with a private key from the Apple Developer account.
/// <see cref="ExternalAuthProviderConfig.ClientId"/> is the Services ID.
/// </summary>
public class AppleAuthProviderConfig : ExternalAuthProviderConfig
{
    /// <summary>The Apple Developer team ID.</summary>
    public string? TeamId { get; set; }

    /// <summary>The ID of the Sign in with Apple key.</summary>
    public string? KeyId { get; set; }

    /// <summary>The key's .p8 file contents (PEM). Use this or <see cref="PrivateKeyPath"/>.</summary>
    public string? PrivateKey { get; set; }

    /// <summary>Path to the key's .p8 file. Use this or <see cref="PrivateKey"/>.</summary>
    public string? PrivateKeyPath { get; set; }

    public override bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(TeamId) &&
        !string.IsNullOrWhiteSpace(KeyId) &&
        (!string.IsNullOrWhiteSpace(PrivateKey) || !string.IsNullOrWhiteSpace(PrivateKeyPath));
}
