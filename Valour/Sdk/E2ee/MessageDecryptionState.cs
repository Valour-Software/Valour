namespace Valour.Sdk.E2ee;

/// <summary>
/// Whether the SDK could read an encrypted message.
/// </summary>
public enum MessageDecryptionState
{
    /// <summary>The message is plain text, or decryption has not run yet.</summary>
    NotAttempted = 0,

    /// <summary>The message was decrypted and its signature and commitment checked.</summary>
    Decrypted = 1,

    /// <summary>
    /// This device does not have the key yet. Other members share it when
    /// they come online; the message decrypts when the key arrives.
    /// </summary>
    WaitingForKey = 2,

    /// <summary>
    /// This device is not set up for encryption, so it cannot read any
    /// encrypted message until it is linked or restored.
    /// </summary>
    DeviceNotVerified = 3,

    /// <summary>
    /// The message failed verification: a bad signature, a mismatched
    /// commitment, or search terms that do not match the text. It is not shown.
    /// </summary>
    Invalid = 4
}
