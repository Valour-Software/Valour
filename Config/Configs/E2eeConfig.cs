namespace Valour.Config.Configs;

/// <summary>
/// End-to-end encryption settings. See Docs/EndToEndEncryption.md.
/// </summary>
public class E2eeConfig
{
    public static E2eeConfig Current { get; private set; } = null!;

    public E2eeConfig()
    {
        Current = this;
    }

    /// <summary>
    /// Days to keep proof of edited and deleted encrypted messages so they can
    /// still be reported. Proofs hold no text. Reported messages keep theirs.
    /// </summary>
    public int ProofRetentionDays { get; set; } = 180;

    /// <summary>
    /// When true, plain-text messages from before encryption are sealed to
    /// their channel's key in the background and the plain text is removed
    /// from the database. Sealing cannot be undone, so it is off until an
    /// operator turns it on after taking a backup. It requires a Data
    /// Protection KEK (<c>DataProtection:Kek</c> or <c>DataProtection:KekFile</c>).
    /// </summary>
    public bool SealLegacyMessages { get; set; } = false;

    /// <summary>Messages sealed per batch by the legacy sealing worker.</summary>
    public int LegacySealBatchSize { get; set; } = 500;

    /// <summary>
    /// Seconds the legacy sealing worker waits after the server starts before
    /// it seals anything, so an instance that is still starting or is about to
    /// replace another one does not take over planets early.
    /// </summary>
    public int LegacySealStartDelaySeconds { get; set; } = 120;

    /// <summary>
    /// Distinct members who must hold a server-created channel key, directly
    /// or through a later key that unlocks it, before the server forgets its
    /// protected copy. Channels with fewer viewers need all of them.
    /// </summary>
    public int HeldKeyMinimumHolders { get; set; } = 3;

    /// <summary>
    /// Open planets with at least this many members replace a channel key
    /// after someone leaves on a schedule rather than immediately. Members
    /// leave large planets constantly, and anyone can join them again.
    /// </summary>
    public int LargePlanetMembers { get; set; } = 1000;

    /// <summary>
    /// How many days a key in a large open planet may stay in use after
    /// someone who holds it leaves.
    /// </summary>
    public int LargePlanetRotationDays { get; set; } = 7;
}
