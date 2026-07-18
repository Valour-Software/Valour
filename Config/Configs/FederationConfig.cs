namespace Valour.Config.Configs;

/// <summary>
/// Federation settings. One binary, two roles:
/// - Hub: mints audience-scoped federation tokens and runs the node registry
///   (the official network, or the center of a self-hosted clone network).
/// - Community node: a self-hosted instance registered with a hub; verifies
///   hub-minted tokens and exchanges them for node-local sessions.
/// </summary>
public class FederationConfig
{
    public static FederationConfig Current;

    public FederationConfig()
    {
        Current = this;
    }

    /// <summary>
    /// True when this instance acts as a federation hub (token minting,
    /// node registry, planet stubs).
    /// </summary>
    public bool HubEnabled { get; set; }

    /// <summary>
    /// Node mode: base URL of the hub this node trusts, e.g. "https://valour.gg".
    /// </summary>
    public string HubUrl { get; set; }

    /// <summary>
    /// Node mode: this node's public domain. Hub-minted tokens are only
    /// accepted when their audience equals this value.
    /// </summary>
    public string NodeDomain { get; set; }

    /// <summary>
    /// Node mode: the verification challenge issued by the hub at
    /// registration. Served at /.well-known/valour-node until verified.
    /// </summary>
    public string NodeChallenge { get; set; }

    /// <summary>
    /// Dev/LAN mode: allow plain-HTTP hub/node URLs and self-signed
    /// certificates for federation calls. Never enable on public deployments.
    /// </summary>
    public bool AllowInsecure { get; set; }

    /// <summary>
    /// True when this instance is configured as a community node.
    /// </summary>
    public bool NodeEnabled =>
        !string.IsNullOrWhiteSpace(HubUrl) && !string.IsNullOrWhiteSpace(NodeDomain);
}
