using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Models.Embeds;
using Valour.Shared;

namespace Valour.Sdk.Services;

public partial class E2eeService
{
    /// <summary>
    /// Encrypts and sends a live update to the embed on a message this
    /// account sent. Set <see cref="EmbedUpdate.NewEmbedContent"/> or
    /// <see cref="EmbedUpdate.ChangedItemsContent"/>, and optionally
    /// <see cref="EmbedUpdate.TargetUserId"/> and <see cref="EmbedUpdate.Revision"/>.
    /// </summary>
    public async Task<TaskResult> SendEmbedUpdateAsync(Message message, EmbedUpdate update)
    {
        if (message is null || update is null)
            return Fail("Include the message and the update.");

        var isFullEmbed = update.NewEmbedContent is not null;
        var content = isFullEmbed ? update.NewEmbedContent : update.ChangedItemsContent;
        if (content is null)
            return Fail("Include a new embed or changed items.");

        var check = isFullEmbed ? EmbedSafety.Check(content) : EmbedSafety.CheckItems(content);
        if (!check.Success)
            return check;

        var channel = await ResolveChannelAsync(message.ChannelId, message.PlanetId);
        if (channel is null)
            return Fail("Channel not found.");

        var node = await channel.Node.CheckAcceptsEncryptedMessagesAsync();
        if (!node.Success)
            return node;

        var key = await EnsureSendKeyAsync(channel);
        if (!key.Success)
            return Fail(key.Message);

        update.TargetMessageId = message.Id;
        update.TargetChannelId = channel.Id;
        update.PlanetId = message.PlanetId;
        update.KeyGeneration = key.Data.Generation;
        update.Encrypted = EmbedUpdateCrypto.Encrypt(key.Data, message.Id, update.TargetUserId ?? 0, update.Revision,
            isFullEmbed, content);

        return await channel.Node.PostAsync("api/embed/update", update);
    }

    /// <summary>
    /// Decrypts a live embed update and checks it with the same rules the
    /// server applies to embeds it can read. The update's key generation must
    /// pass <see cref="EmbedUpdateViolation"/>. Returns false when the update
    /// cannot be read or is not allowed, in which case it must be ignored.
    /// </summary>
    public async Task<bool> DecryptEmbedUpdateAsync(EmbedUpdate update)
    {
        if (update?.Encrypted is null || !CanDecrypt)
            return false;

        var channel = await ResolveChannelAsync(update.TargetChannelId, update.PlanetId);
        if (channel is null)
            return false;

        // Updates are shown only on messages the app has, so an update for a
        // message that is not cached has nothing to change.
        var scope = channel.Node?.IsExternal == true ? channel.Node.Name : null;
        if (!_client.Cache.Messages.TryGet(update.TargetMessageId, scope, out var target) || target is null ||
            target.ChannelId != channel.Id)
            return false;

        var ring = await GetKeyRingAsync(channel);
        var record = ring is null ? null : await GetGenerationRecordAsync(channel, update.KeyGeneration);
        var violation = EmbedUpdateViolation(update.KeyGeneration, target.KeyGeneration, record,
            ring?.Trusted.ContainsKey(update.KeyGeneration) == true);
        if (violation is not null)
        {
            LogWarning($"Ignoring an embed update for message {update.TargetMessageId}: {violation}");
            return false;
        }

        var secret = await GetSecretAsync(channel, update.KeyGeneration);
        if (secret is null)
            return false;

        try
        {
            var (isFullEmbed, content) = EmbedUpdateCrypto.Decrypt(secret, update.TargetMessageId,
                update.TargetUserId ?? 0, update.Revision, update.Encrypted);

            var check = isFullEmbed ? EmbedSafety.Check(content) : EmbedSafety.CheckItems(content);
            if (!check.Success)
            {
                LogWarning($"Ignoring an embed update for message {update.TargetMessageId}: {check.Message}");
                return false;
            }

            update.NewEmbedContent = isFullEmbed ? content : null;
            update.ChangedItemsContent = isFullEmbed ? null : content;
            return true;
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            LogWarning($"Could not decrypt an embed update for message {update.TargetMessageId}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns why an embed update's key may not be used, or null when it may.
    /// Updates are not signed, so anyone holding the key they use could write
    /// one. The key must be at least as new as the message's, so the server
    /// cannot replay an old key that someone removed still holds; it must be a
    /// member's key, since the server knew the keys it created; and this device
    /// must trust its creator, as it would before sending with it.
    /// </summary>
    internal static string EmbedUpdateViolation(int updateGeneration, int messageGeneration,
        ChannelKeyGenerationRecord record, bool trusted)
    {
        if (updateGeneration < messageGeneration)
            return "The update uses an older key than the message.";
        if (record is null)
            return "The update's key is unknown.";
        if (record.IsServerCreated)
            return "The update uses a key the server created.";
        return trusted ? null : "The update uses a key this device does not trust.";
    }
}
