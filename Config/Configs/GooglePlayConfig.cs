namespace Valour.Config.Configs;

/// <summary>
/// Google Play Billing for the Android app. Purchases are verified through the
/// Google Play Developer API with a service account that has been granted
/// access in the Play Console. Billing is offered only when the package name
/// and service account are both set.
/// </summary>
public class GooglePlayConfig
{
    public static GooglePlayConfig? Current;

    public GooglePlayConfig()
    {
        Current = this;
    }

    /// <summary>The Android application ID, for example gg.valour.app.</summary>
    public string? PackageName { get; set; }

    /// <summary>Path to the service account JSON key used for the Developer API.</summary>
    public string? ServiceAccountPath { get; set; }

    /// <summary>
    /// Shared secret that Pub/Sub must send as the <c>token</c> query parameter
    /// on real-time developer notifications.
    /// </summary>
    public string? NotificationToken { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PackageName) && !string.IsNullOrWhiteSpace(ServiceAccountPath);
}
