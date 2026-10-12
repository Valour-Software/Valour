namespace Valour.Config.Configs;

/// <summary>
/// App Store In-App Purchase for the iOS app. Purchases are verified through
/// the App Store Server API with an In-App Purchase key from App Store Connect
/// (Users and Access, Integrations, In-App Purchase). Billing is offered only
/// when the bundle ID, issuer ID, key ID and key are all set.
/// </summary>
public class AppStoreConfig
{
    public static AppStoreConfig? Current;

    public AppStoreConfig()
    {
        Current = this;
    }

    /// <summary>The iOS bundle ID, for example gg.valour.app.</summary>
    public string? BundleId { get; set; }

    /// <summary>The issuer ID shown above the keys in App Store Connect.</summary>
    public string? IssuerId { get; set; }

    /// <summary>The In-App Purchase key ID.</summary>
    public string? KeyId { get; set; }

    /// <summary>The key's .p8 file contents (PEM). Use this or <see cref="PrivateKeyPath"/>.</summary>
    public string? PrivateKey { get; set; }

    /// <summary>Path to the key's .p8 file. Use this or <see cref="PrivateKey"/>.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Shared secret that App Store Server Notifications must send as the
    /// <c>token</c> query parameter. Put it in the notification URL in App
    /// Store Connect.
    /// </summary>
    public string? NotificationToken { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BundleId) &&
        !string.IsNullOrWhiteSpace(IssuerId) &&
        !string.IsNullOrWhiteSpace(KeyId) &&
        (!string.IsNullOrWhiteSpace(PrivateKey) || !string.IsNullOrWhiteSpace(PrivateKeyPath));
}
