using Valour.Sdk.E2ee;
using Valour.Sdk.Models;

namespace Valour.Client.Messages;

public static class MessageReadability
{
    /// <summary>
    /// True when a message's text may be shown, copied, or edited: it is not
    /// encrypted, or it was decrypted and passed verification. A message that
    /// failed verification can still carry decrypted text, which must never
    /// be shown because its sender may have tampered with it.
    /// </summary>
    public static bool HasReadableText(this Message message) =>
        message is not null &&
        (!message.IsEncrypted || message.DecryptionState == MessageDecryptionState.Decrypted);
}
