namespace Valour.Shared.Models;

/// <summary>
/// Self-describing manifest served at /.well-known/valour-instance.
/// Clients read this at startup to learn the instance's hosts and which
/// optional services are available, adapting the UI accordingly. This is
/// also the node descriptor used by federation.
/// </summary>
public class InstanceManifest
{
    /// <summary>
    /// The current federation protocol version spoken by this server build.
    /// </summary>
    public const int CurrentProtocolVersion = 1;

    /// <summary>
    /// Display name of the instance
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Server version (assembly version)
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Federation protocol version this instance speaks
    /// </summary>
    public int ProtocolVersion { get; set; } = CurrentProtocolVersion;

    /// <summary>
    /// True for the official Valour deployment; false for self-hosted instances
    /// and community nodes.
    /// </summary>
    public bool IsOfficial { get; set; }

    public InstanceHosts Hosts { get; set; }

    public InstanceCapabilities Capabilities { get; set; }

    /// <summary>
    /// Upload cap for users without a subscription, in bytes
    /// </summary>
    public long DefaultMaxUploadBytes { get; set; }
}

public class InstanceHosts
{
    public string RootDomain { get; set; }
    public string App { get; set; }
    public string Api { get; set; }
    public string Threads { get; set; }
    public string ContentCdn { get; set; }
    public string PublicCdn { get; set; }
}

/// <summary>
/// Which optional services this instance has configured. Clients hide or
/// disable the corresponding UI when a capability is off.
/// </summary>
public class InstanceCapabilities
{
    /// <summary>
    /// Transactional email (verification emails, password resets)
    /// </summary>
    public bool Email { get; set; }

    /// <summary>
    /// Stripe payments (subscriptions, Valour Credit purchases)
    /// </summary>
    public bool Payments { get; set; }

    /// <summary>
    /// Voice and video channels
    /// </summary>
    public bool Voice { get; set; }

    /// <summary>
    /// Push notifications (Web Push / FCM)
    /// </summary>
    public bool PushNotifications { get; set; }

    /// <summary>
    /// Media hash-match safety scanning
    /// </summary>
    public bool MediaSafety { get; set; }

    /// <summary>
    /// Whether new account registration is open
    /// </summary>
    public bool OpenRegistration { get; set; } = true;
}
