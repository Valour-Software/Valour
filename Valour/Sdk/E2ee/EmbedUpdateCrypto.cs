namespace Valour.Sdk.E2ee;

/// <summary>
/// Encrypts live embed updates a bot pushes to a message it sent. The update
/// is encrypted with a key derived from the channel key, so the server only
/// relays it. The associated data binds the channel, message, recipient, and
/// revision, so the server cannot move an update to another message or user.
/// </summary>
public static class EmbedUpdateCrypto
{
    public const int MaxEncryptedLength = 80 * 1024;

    private static byte[] Key(ChannelKeySecret channelKey) =>
        E2eeCrypto.Hkdf(channelKey.ContentKey, null, "valour-e2ee/embed-update/v1");

    private static byte[] AssociatedData(long channelId, long messageId, long targetUserId, long revision,
        int generation) =>
        new E2eeWriter()
            .WriteMagic("VEU1")
            .WriteInt64(channelId)
            .WriteInt64(messageId)
            .WriteInt64(targetUserId)
            .WriteInt64(revision)
            .WriteInt32(generation)
            .ToArray();

    /// <summary>
    /// Encrypts either a full embed or a list of changed items.
    /// </summary>
    public static byte[] Encrypt(ChannelKeySecret channelKey, long messageId, long targetUserId, long revision,
        bool isFullEmbed, string content)
    {
        var plaintext = new E2eeWriter()
            .WriteMagic("VEP1")
            .WriteBool(isFullEmbed)
            .WriteString(content ?? string.Empty)
            .ToArray();

        return E2eeCrypto.Encrypt(Key(channelKey), plaintext,
            AssociatedData(channelKey.ChannelId, messageId, targetUserId, revision, channelKey.Generation));
    }

    public static (bool IsFullEmbed, string Content) Decrypt(ChannelKeySecret channelKey, long messageId,
        long targetUserId, long revision, byte[] encrypted)
    {
        var plaintext = E2eeCrypto.Decrypt(Key(channelKey), encrypted,
            AssociatedData(channelKey.ChannelId, messageId, targetUserId, revision, channelKey.Generation));

        var reader = new E2eeReader(plaintext);
        reader.ReadMagic("VEP1");
        var isFullEmbed = reader.ReadBool();
        var content = reader.ReadString();
        reader.EnsureEnd();
        return (isFullEmbed, content);
    }
}
