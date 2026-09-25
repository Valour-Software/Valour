using Valour.Sdk.E2ee;
using Valour.Sdk.Models;

namespace Valour.Client.Messages;

public static class MessageReadability
{
    /// <summary>
    /// True when a message's text may be shown, copied, or edited: it is plain
    /// text that passed its checks, or it was decrypted and passed
    /// verification. A message that failed verification can still carry
    /// text, which must never be shown because it may have been tampered with.
    /// </summary>
    public static bool HasReadableText(this Message message) =>
        message is not null &&
        (message.IsEncrypted
            ? message.DecryptionState == MessageDecryptionState.Decrypted
            : message.DecryptionState == MessageDecryptionState.NotAttempted);
}
