using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Models.Embeds;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public partial class E2eeService
{
    /// <summary>
    /// A message that could not be decrypted yet. The model cache holds the
    /// instance the app shows, so it is found there by ID and cache scope; the
    /// weak reference covers messages that were never cached, such as search
    /// results.
    /// </summary>
    private sealed class PendingMessage
    {
        public WeakReference<Message> Reference { get; init; }
        public string Scope { get; init; }
        public long AuthorUserId { get; init; }

        /// <summary>
        /// True when the message waits for something other than a key share,
        /// such as the author's key log or a server that could not be reached,
        /// so it is retried on a timer.
        /// </summary>
        public bool RetryLater { get; init; }
    }

    // Messages waiting to be decrypted, by channel. Waiting for a key ends
    // when the key arrives; other waits are retried on a timer.
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), ConcurrentDictionary<long, PendingMessage>> _pendingMessages = new();

    private const int MaxPendingPerChannel = 1000;
    private const int MaxPendingChannels = 500;
    private const int MaxUserStates = 5000;
    private static readonly TimeSpan FirstPendingRetry = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LongestPendingRetry = TimeSpan.FromMinutes(5);

    // Channels with a timed retry scheduled, and how many retries in a row
    // found nothing new.
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), int> _pendingRetryAttempts = new();
    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), byte> _scheduledPendingRetries = new();

    // When each user's key log was last loaded because one of their messages
    // named a device this device did not know.
    private readonly ConcurrentDictionary<long, DateTime> _authorRefreshes = new();
    private static readonly TimeSpan AuthorRefreshInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How far a server-sealed message's time may differ from the time its
    /// header was attested with.
    /// </summary>
    private const long SealedTimeToleranceMs = 2000;

    // Sending

    /// <summary>
    /// Sends a message, encrypting it on this device. The channel's first key
    /// is created if it has none. The message instance passed in is not
    /// changed, so a preview in the UI keeps its text.
    /// </summary>
    public async Task<TaskResult<Message>> SendMessageAsync(Message message)
    {
        if (CheckContentLength(message) is { Success: false } tooLong)
            return TaskResult<Message>.FromFailure(tooLong.Message);

        var channel = await ResolveChannelAsync(message.ChannelId, message.PlanetId);
        if (channel is null)
            return TaskResult<Message>.FromFailure("Channel not found.");

        var allowed = await CheckSendAllowedAsync(message, channel);
        if (!allowed.Success)
            return TaskResult<Message>.FromFailure(allowed.Message);

        var ready = RequireReadyToSend();
        if (!ready.Success)
            return TaskResult<Message>.FromFailure(ready.Message);

        return await SendEncryptedAsync(message, channel, revision: 0, nonce: E2eeCrypto.RandomBytes(16), isEdit: false);
    }

    /// <summary>
    /// Saves an edit, encrypting it as the message's next revision. An author
    /// can replace one of their own messages the server sealed from before
    /// encryption; webhook and system messages cannot be edited.
    /// </summary>
    public async Task<TaskResult<Message>> EditMessageAsync(Message message)
    {
        if (CheckContentLength(message) is { Success: false } tooLong)
            return TaskResult<Message>.FromFailure(tooLong.Message);

        var channel = await ResolveChannelAsync(message.ChannelId, message.PlanetId);
        if (channel is null)
            return TaskResult<Message>.FromFailure("Channel not found.");

        var allowed = await CheckSendAllowedAsync(message, channel);
        if (!allowed.Success)
            return TaskResult<Message>.FromFailure(allowed.Message);

        var ready = RequireReadyToSend();
        if (!ready.Success)
            return TaskResult<Message>.FromFailure(ready.Message);

        var nonce = E2eeCrypto.RandomBytes(16);
        var revision = 0;
        if (message.EncryptionVersion == MessageEncryption.EndToEnd && message.Envelope is not null)
        {
            var (_, header) = MessageCrypto.ReadHeader(message.Envelope);
            nonce = header.MessageNonce;
            revision = header.Revision + 1;
        }
        else if (message.EncryptionVersion == MessageEncryption.ServerSealed && message.SealedKind != ServerSealedKind.Legacy)
        {
            return TaskResult<Message>.FromFailure("Messages sealed by the server cannot be edited.");
        }

        return await SendEncryptedAsync(message, channel, revision, nonce, isEdit: true);
    }

    private TaskResult RequireReadyToSend() => Status switch
    {
        E2eeStatus.Ready => TaskResult.SuccessResult,
        E2eeStatus.Error => Fail("Your encryption keys failed verification, so this device cannot send messages."),
        E2eeStatus.Unavailable => Fail("Your encryption keys could not be loaded because Valour could not be " +
                                       "reached. Valour tries again automatically."),
        _ => Fail("Verify this device to send messages. Messages are end-to-end encrypted.")
    };

    /// <summary>
    /// Refuses text longer than the server accepts for any message. Reports
    /// carry the text, so a longer message could not be reported.
    /// </summary>
    private static TaskResult CheckContentLength(Message message) =>
        (message.Content?.Length ?? 0) > MessageCrypto.MaxContentLength
            ? Fail($"Messages can be at most {MessageCrypto.MaxContentLength} characters.")
            : TaskResult.SuccessResult;

    /// <summary>
    /// Applies the checks the server makes on text it can read and cannot
    /// make on encrypted text: the node must accept encrypted messages, an
    /// embed needs the channel's Embed permission, and custom planet emojis
    /// need the planet's permission and cannot be used in direct chats.
    /// </summary>
    private async Task<TaskResult> CheckSendAllowedAsync(Message message, Channel channel)
    {
        var node = await channel.Node.CheckAcceptsEncryptedMessagesAsync();
        if (!node.Success)
            return node;

        var hasEmbed = message.EmbedAttachment is not null;
        var hasCustomEmoji = PlanetEmojiText.ExtractCustomEmojiIds(message.Content).Count > 0;
        if (!hasEmbed && !hasCustomEmoji)
            return TaskResult.SuccessResult;

        if (channel.PlanetId is null)
        {
            return hasCustomEmoji
                ? Fail("Custom planet emojis can only be used in planet channels.")
                : TaskResult.SuccessResult;
        }

        var member = await channel.Planet.FetchMemberByUserAsync(_client.Me.Id);
        if (member is null)
            return Fail("You are not a member of the planet this channel belongs to.");

        if (hasEmbed && !await channel.HasPermissionAsync(member, Valour.Shared.Authorization.ChatChannelPermissions.Embed))
            return Fail("You lack permission to attach embeds to messages in this channel.");

        if (hasCustomEmoji && !member.HasPermission(Valour.Shared.Authorization.PlanetPermissions.UseCustomEmojis))
            return Fail("You lack permission to use custom emojis in this planet.");

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// A copy of an attachment to send, so the instance the app shows is not
    /// changed. A missing MIME type or file name is sent empty, because the
    /// server fills in missing values for uploaded files and recipients check
    /// the values the sender listed.
    /// </summary>
    private static MessageAttachment WireAttachment(MessageAttachment attachment) => new(attachment.Type)
    {
        Id = attachment.Id,
        MessageId = attachment.MessageId,
        SortOrder = attachment.SortOrder,
        Location = attachment.Location,
        MimeType = attachment.MimeType ?? string.Empty,
        FileName = attachment.FileName ?? string.Empty,
        CdnBucketItemId = attachment.CdnBucketItemId,
        Width = attachment.Width,
        Height = attachment.Height,
        Inline = attachment.Inline,
        Missing = attachment.Missing,
        Data = attachment.Data,
        OpenGraph = attachment.OpenGraph,
        PlanetHosted = attachment.PlanetHosted,
        ReportedSha256 = attachment.ReportedSha256,
        IsSpoiler = attachment.IsSpoiler
    };

    private async Task<TaskResult<Message>> SendEncryptedAsync(Message message, Channel channel, int revision,
        byte[] nonce, bool isEdit)
    {
        var result = TaskResult<Message>.FromFailure("The message could not be sent.");

        // A stale key or a required rotation is fixed and retried once.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var keyResult = await EnsureSendKeyAsync(channel);
            if (!keyResult.Success)
                return TaskResult<Message>.FromFailure(keyResult.Message);

            var key = keyResult.Data;
            var indexSecret = await GetIndexSecretAsync(channel, key.Generation) ?? key;

            var content = message.Content ?? string.Empty;
            var embed = message.EmbedAttachment?.Data;

            // The server cannot check an encrypted embed, so it is checked here
            // and again by every recipient.
            if (embed is not null && EmbedSafety.Check(embed) is { Success: false } embedCheck)
                return TaskResult<Message>.FromFailure(embedCheck.Message);
            var terms = SearchTerms.ForMessage(indexSecret.IndexKey, channel.Id, content);

            // Files go beside the ciphertext, so the payload lists them for
            // recipients to check. Link previews are generated again by the
            // server from the listed links, and embeds travel in the payload.
            var attachments = message.Attachments?
                .Where(AttachmentBinding.IsBound)
                .Select(WireAttachment)
                .ToList();
            if (attachments?.Count > AttachmentBinding.MaxAttachments)
                return TaskResult<Message>.FromFailure(
                    $"A message can have at most {AttachmentBinding.MaxAttachments} attachments.");

            var (envelope, _) = MessageCrypto.Seal(key, channel.PlanetId ?? 0, _client.Me.Id, Device, nonce,
                revision, message.ReplyToId ?? 0, content, embed, terms, NowMs(), AttachmentBinding.Digests(attachments));

            var wire = message.CreateWireCopy();
            wire.Content = string.Empty;
            wire.EncryptionVersion = MessageEncryption.EndToEnd;
            wire.Envelope = envelope;
            wire.KeyGeneration = key.Generation;
            wire.SearchTerms = terms;
            wire.PreviewUrls = MessagePreviewUrls.Extract(content);

            // Mentions always come from the text, so an edit that removes a
            // mention removes its notification, and nobody is notified of a
            // mention the text does not show.
            wire.Mentions = MentionTextParser.Parse(content);
            wire.CustomEmojiIds = PlanetEmojiText.ExtractCustomEmojiIds(content).ToList();
            wire.Attachments = attachments is { Count: > 0 } ? attachments : null;

            result = isEdit
                ? await channel.Node.PutAsyncWithResponse<Message>(message.IdRoute, wire)
                : await channel.Node.PostAsyncWithResponse<Message>(message.BaseRoute, wire);

            if (result.Success)
                break;

            var retry = E2eeErrorCodes.Is(result.Message, E2eeErrorCodes.RotationRequired) ||
                        E2eeErrorCodes.Is(result.Message, E2eeErrorCodes.StaleGeneration);
            if (!retry)
                return result;

            _keyRings.TryRemove(ChannelKey(channel), out _);
        }

        if (!result.Success)
            return result;

        // The server echoes the stored message; this device already knows its
        // text. The cached instance is returned so callers can keep using it.
        await DecryptAsync(result.Data);
        return TaskResult<Message>.FromData(result.Data.Sync(_client));
    }

    // Receiving

    /// <summary>
    /// Decrypts messages before they are cached, including reply previews.
    /// Messages whose key has not arrived are marked as waiting and decrypted
    /// in place when it does. The authors' key logs are loaded in one request,
    /// and the work yields between messages so a browser stays responsive
    /// while it decrypts a page of history.
    /// </summary>
    public async Task DecryptAllAsync(IEnumerable<Message> messages)
    {
        if (messages is null)
            return;

        var list = messages.Where(m => m is not null).ToList();
        if (list.Count == 0)
            return;

        await PrefetchAuthorsAsync(list.Concat(list.Select(m => m.ReplyTo).Where(r => r is not null)));

        var decrypted = new Dictionary<long, Message>();
        foreach (var message in list)
        {
            await DecryptAsync(message, decryptReply: false);
            decrypted.TryAdd(message.Id, message);
            await Task.Yield();
        }

        // A reply preview is often another message on the same page; its
        // decrypted state is copied rather than computed again.
        foreach (var message in list)
        {
            var reply = message.ReplyTo;
            if (reply is null)
                continue;

            if (decrypted.TryGetValue(reply.Id, out var source) && reply.IsEncrypted && source.Envelope is not null &&
                reply.Envelope is not null && reply.Envelope.AsSpan().SequenceEqual(source.Envelope) &&
                source.DecryptionState == MessageDecryptionState.Decrypted)
            {
                reply.CopyDecryptionFrom(source);
                continue;
            }

            await DecryptAsync(reply);
            await Task.Yield();
        }

        TrimCaches();
    }

    /// <summary>
    /// Loads the key logs of the authors of end-to-end encrypted messages in
    /// one request, instead of one request per author.
    /// </summary>
    private async Task PrefetchAuthorsAsync(IEnumerable<Message> messages)
    {
        if (!CanDecrypt)
            return;

        var authors = messages
            .Where(m => m.EncryptionVersion == MessageEncryption.EndToEnd)
            .Select(m => m.AuthorUserId)
            .Distinct()
            .ToList();
        if (authors.Count < 2)
            return;

        try
        {
            await GetUserStatesAsync(authors);
        }
        catch (Exception e)
        {
            LogWarning($"Could not load the key logs of message authors: {e.Message}");
        }
    }

    private int _decryptCount;

    public Task DecryptAsync(Message message)
    {
        // Messages that arrive one at a time never pass through DecryptAllAsync,
        // so the caches are also trimmed after every few hundred messages.
        if (Interlocked.Increment(ref _decryptCount) % 500 == 0)
            TrimCaches();
        return DecryptAsync(message, decryptReply: true);
    }

    private async Task DecryptAsync(Message message, bool decryptReply)
    {
        if (message is null)
            return;

        if (decryptReply && message.ReplyTo is not null)
            await DecryptAsync(message.ReplyTo);

        if (!message.IsEncrypted)
        {
            await CheckPlainTextAsync(message);
            return;
        }

        // Embeds on an encrypted message come only from its encrypted payload.
        // An embed attachment the server returned in the clear is not the
        // sender's and is never shown.
        message.Attachments?.RemoveAll(a => a.Type == MessageAttachmentType.Embed);

        try
        {
            await DecryptCoreAsync(message);
        }
        catch (Exception e) when (e is E2eeVerificationException or E2eeFormatException)
        {
            MarkInvalid(message, e.Message);
        }
        catch (E2eeUnsupportedException)
        {
            message.Content = string.Empty;
            message.Mentions = null;
            message.DecryptionState = MessageDecryptionState.NeedsNewerVersion;
            message.DecryptionError = "Update Valour to see this message.";
        }
        catch (Exception e)
        {
            LogError($"Failed to decrypt message {message.Id}", e);
            message.Content = string.Empty;
            MarkWaiting(message, "This message could not be decrypted yet.", retryLater: true);
        }
    }

    /// <summary>
    /// Checks a plain-text message the server returned. Only messages from
    /// before encryption are plain, but the server could return any message
    /// that way, so the text is labelled as not signed by its author and must
    /// follow the rule for sealed messages from before encryption: it must be
    /// older than the first key a member created in the channel. A device
    /// that is not set up for encryption cannot check the channel's keys, so
    /// it only shows the label; a device whose key load failed waits and tries
    /// again.
    /// </summary>
    private async Task CheckPlainTextAsync(Message message)
    {
        message.DecryptionState = MessageDecryptionState.NotAttempted;
        message.DecryptionError = null;

        // Messages this app builds before sending are not from the server.
        if (message.Id == 0)
            return;

        message.IsPlainTextHistory = true;
        if (Status != E2eeStatus.Ready)
            return;

        var channel = await ResolveChannelAsync(message.ChannelId, message.PlanetId);
        var ring = channel is null ? null : await GetKeyRingAsync(channel);
        if (ring is null)
        {
            MarkWaiting(message, "Checking the channel's key history. This message is shown once it loads.",
                retryLater: true, channel: channel);
            return;
        }

        if (Math.Max(ring.LatestGeneration, await GetGenerationPinAsync(channel)) == 0)
            return;

        var (firstMemberRecord, unavailable) = await GetFirstMemberRecordAsync(channel);
        if (unavailable)
        {
            MarkWaiting(message, "Checking the channel's key history. This message is shown once it loads.",
                retryLater: true, channel: channel);
            return;
        }

        if (firstMemberRecord is not null &&
            ToUnixMs(message.TimeSent) >= firstMemberRecord.TimestampMs + LegacyAllowanceMs)
        {
            MarkInvalid(message, "This message was not encrypted, but it is newer than the channel's encryption.");
            return;
        }

        ForgetPending(message);
    }

    /// <summary>
    /// True when this device may try to decrypt: it is ready, or its keys were
    /// reset elsewhere and it still holds the keys it had before.
    /// </summary>
    private bool CanDecrypt => Status == E2eeStatus.Ready || (KeysResetElsewhere && Device is not null);

    private async Task DecryptCoreAsync(Message message)
    {
        // Content on an encrypted message is never trusted as text.
        message.Content = string.Empty;
        message.SignedContent = null;
        message.SignedEmbeds = null;

        if (Status == E2eeStatus.Unavailable)
        {
            MarkWaiting(message, "Your encryption keys could not be loaded yet. Valour tries again automatically.");
            return;
        }

        if (!CanDecrypt)
        {
            message.DecryptionState = MessageDecryptionState.DeviceNotVerified;
            message.DecryptionError = "Verify this device to read encrypted messages.";
            return;
        }

        var channel = await ResolveChannelAsync(message.ChannelId, message.PlanetId);
        if (channel is null)
        {
            MarkWaiting(message, "The channel for this message could not be loaded.", retryLater: true);
            return;
        }

        var ring = await GetKeyRingAsync(channel);
        var secret = ring is null ? null : await GetSecretAsync(channel, message.KeyGeneration);
        if (secret is null)
        {
            // When the channel's keys could not be loaded, no key notice may
            // come, so the message is tried again on a timer.
            var fetchFailed = ring is null || _keyRingFailures.ContainsKey(ChannelKey(channel));
            MarkWaiting(message, fetchFailed
                    ? "The channel's keys could not be loaded. Retrying automatically..."
                    : "Waiting for e2ee keys from another member...",
                retryLater: fetchFailed, channel: channel);
            return;
        }

        if (message.EncryptionVersion == MessageEncryption.EndToEnd)
        {
            var (_, header) = MessageCrypto.ReadHeader(message.Envelope);
            if (header.AuthorUserId != message.AuthorUserId)
                throw new E2eeVerificationException("The message does not match its signed author.");
            CheckHeaderMatchesMessage(message, header.PlanetId, header.ReplyToId);

            var (author, authorUnavailable) =
                await ResolveAuthorAsync(message.AuthorUserId, header.AuthorDeviceId, message.TimeSent);
            if (authorUnavailable)
            {
                MarkWaiting(message, "Checking who sent this. The message appears once that's done.",
                    retryLater: true, channel: channel);
                return;
            }

            var opened = MessageCrypto.Open(message.Envelope, secret, author, message.TimeSent);
            IReadOnlyList<string> signedEmbeds = opened.Payload.Embed is null ? [] : [opened.Payload.Embed];

            // Search and automod rely on the terms the author uploaded. If they
            // do not match the text, the author tried to hide words.
            var indexSecret = await GetIndexSecretAsync(channel, message.KeyGeneration);
            if (indexSecret is not null)
            {
                var terms = SearchTerms.ForMessage(indexSecret.IndexKey, channel.Id, opened.Payload.Content);
                if (!E2eeCrypto.FixedTimeEquals(SearchTerms.Hash(terms), opened.Header.TermsHash))
                {
                    // The text is kept so the message can be reported with
                    // proof, but apps never display messages in this state.
                    MarkInvalid(message, "This message was hidden because its sender tampered with automod checks.");
                    SetDecryptedContent(message, opened.Payload.Content);
                    message.SignedEmbeds = signedEmbeds;
                    message.FrankingKey = opened.Payload.FrankingKey;
                    message.EncryptedRevision = opened.Header.Revision;
                    TamperedMessageDetected?.Invoke(message);
                    return;
                }
            }

            SetDecryptedContent(message, opened.Payload.Content);
            message.SignedEmbeds = signedEmbeds;
            message.FrankingKey = opened.Payload.FrankingKey;
            message.EncryptedRevision = opened.Header.Revision;
            ApplyAttachmentBinding(message, opened.Payload);
            await SetDecryptedEmbedsAsync(message, channel, signedEmbeds, checkAuthorPermission: true);
        }
        else if (message.EncryptionVersion == MessageEncryption.ServerSealed)
        {
            var (header, _, _) = ServerSealing.Read(message.Envelope);
            message.SealedKind = header.Kind;
            CheckHeaderMatchesMessage(message, header.PlanetId, replyToId: null);
            if (!await CheckSealedHeaderAsync(message, channel, header))
                return;

            var (serverKey, serverKeyUnavailable) = await TryResolveServerKeyAsync(channel.Node, header.ServerKeyId);
            if (serverKey is null)
            {
                if (!serverKeyUnavailable)
                    throw new E2eeVerificationException("The message was sealed with a key its server does not publish.");

                MarkWaiting(message, "Checking the server's signature. This message is shown once it loads.",
                    retryLater: true, channel: channel);
                return;
            }

            var payload = ServerSealing.Open(message.Envelope, secret, _ => serverKey);
            if (payload.Content.Length > MessageCrypto.MaxContentLength)
                throw new E2eeVerificationException("Message is longer than messages may be.");

            SetDecryptedContent(message, payload.Content);
            message.SignedEmbeds = payload.Embeds;
            message.SealedSalt = payload.Salt;
            await SetDecryptedEmbedsAsync(message, channel, payload.Embeds, checkAuthorPermission: false);
        }
        else
        {
            throw new E2eeFormatException("Unsupported encryption version.");
        }

        message.Mentions = MentionsInText(message.Mentions, message.SignedContent);
        message.DecryptionState = MessageDecryptionState.Decrypted;
        message.DecryptionError = null;
        ForgetPending(message);
    }

    /// <summary>
    /// Checks the metadata the server stores beside a message against the
    /// signed header. A message may not claim to reply when its header says it
    /// does not. Otherwise reply IDs are not compared: a planet that moves
    /// between nodes gives its messages new IDs, and the server clears the
    /// reference when the reply target is deleted or its author is blocked.
    /// </summary>
    private static void CheckHeaderMatchesMessage(Message message, long planetId, long? replyToId)
    {
        if (planetId != (message.PlanetId ?? 0))
            throw new E2eeVerificationException("The message does not belong to the planet it was sent in.");

        if (replyToId == 0 && (message.ReplyToId ?? 0) != 0)
            throw new E2eeVerificationException("The message was not sent as a reply.");
    }

    /// <summary>
    /// Applies the rules that keep the server from posting as other people.
    /// Webhook and system messages are posted as Victor, the system account;
    /// messages from before encryption must be sealed with the channel's first
    /// key and predate the first key a member created; and the time shown must
    /// be the time the server attested. Returns false when the message was
    /// marked as waiting.
    /// </summary>
    private async Task<bool> CheckSealedHeaderAsync(Message message, Channel channel, ServerSealedHeader header)
    {
        ChannelKeyGenerationRecord firstMemberRecord = null;
        if (header.Kind == ServerSealedKind.Legacy && header.Generation == 1 && message.KeyGeneration == 1)
        {
            var (record, unavailable) = await GetFirstMemberRecordAsync(channel);
            if (unavailable)
            {
                MarkWaiting(message, "Checking the channel's key history. This message is shown once it loads.",
                    retryLater: true, channel: channel);
                return false;
            }
            firstMemberRecord = record;
        }

        var violation = SealedHeaderViolation(header, message.AuthorUserId, message.TimeSent, message.KeyGeneration,
            firstMemberRecord);
        if (violation is not null)
            throw new E2eeVerificationException(violation);
        return true;
    }

    /// <summary>
    /// Returns the record of the channel's first key a member created: the
    /// first generation, or the second when the server created the first.
    /// Only the first generation can be server-created. The record is null
    /// when no member has created a key yet, and unavailable is true when a
    /// record this device needs could not be loaded.
    /// </summary>
    private async Task<(ChannelKeyGenerationRecord Record, bool Unavailable)> GetFirstMemberRecordAsync(Channel channel)
    {
        var first = await GetGenerationRecordAsync(channel, 1);
        if (first is null)
            return (null, true);
        if (!first.IsServerCreated)
            return (first, false);

        // The newest generation this device has seen counts too, so the
        // server cannot hide the second key by reporting an older newest one.
        var ring = await GetKeyRingAsync(channel);
        var latest = Math.Max(ring?.LatestGeneration ?? 0, await GetGenerationPinAsync(channel));
        if (latest < 2)
            return (null, false);

        var second = await GetGenerationRecordAsync(channel, 2);
        return second is null ? (null, true) : (second, false);
    }

    private const long LegacyAllowanceMs = 60 * 60 * 1000;

    /// <summary>
    /// Returns why a server-sealed message breaks the rules that keep the
    /// server from posting as other people, or null when it follows them.
    /// <paramref name="firstMemberRecord"/> is the channel's first key a
    /// member created, or null when there is none yet, and is only needed for
    /// <see cref="ServerSealedKind.Legacy"/> messages.
    /// </summary>
    internal static string SealedHeaderViolation(ServerSealedHeader header, long authorUserId, DateTime timeSent,
        int keyGeneration, ChannelKeyGenerationRecord firstMemberRecord)
    {
        if (header.AuthorUserId != authorUserId)
            return "The sealed message does not match its header.";

        if (Math.Abs(ToUnixMs(timeSent) - header.TimeSentMs) > SealedTimeToleranceMs)
            return "The sealed message's time does not match its header.";

        switch (header.Kind)
        {
            case ServerSealedKind.Webhook:
            case ServerSealedKind.System:
                return header.AuthorUserId == Valour.Shared.Models.ISharedUser.VictorUserId
                    ? null
                    : "Only the system account can post webhook and system messages.";

            case ServerSealedKind.Legacy:
                // Text from before encryption is always sealed with the
                // channel's first key. The first key a member created proves
                // when encryption began, so the text must be older; the
                // server knew its own key, so that key proves nothing. The
                // allowance covers a slow clock on the creator's device and
                // plain text an older server wrote during an upgrade.
                if (header.Generation != 1 || keyGeneration != 1)
                    return "This message claims to be from before encryption but was sealed with a later key.";
                if (firstMemberRecord is not null && !firstMemberRecord.IsServerCreated &&
                    header.TimeSentMs >= firstMemberRecord.TimestampMs + LegacyAllowanceMs)
                    return "This message claims to be from before encryption but is newer than the channel's encryption.";
                return null;

            default:
                return "Unknown kind of server-sealed message.";
        }
    }

    /// <summary>
    /// Keeps only the attachments the author listed in the signed payload and
    /// the link previews generated from links in the text. A preview of a
    /// link inside a spoiler is shown as a spoiler. When the server returned
    /// a file the author did not list, it is not shown and the message says so.
    /// </summary>
    private void ApplyAttachmentBinding(Message message, MessagePayload payload)
    {
        if (message.Attachments is null || message.Attachments.Count == 0)
            return;

        var (kept, removed) = AttachmentBinding.Filter(message.Attachments, payload.AttachmentDigests);
        if (removed)
        {
            LogWarning($"Hiding attachments on message {message.Id} that its author did not send.");
            message.AttachmentsWithheld = true;
        }

        var previewUrls = MessagePreviewUrls.Extract(payload.Content);
        var usedUrls = new HashSet<string>(StringComparer.Ordinal);
        kept.RemoveAll(attachment =>
        {
            if (!attachment.Inline || attachment.Type == MessageAttachmentType.Embed)
                return false;

            var source = InlinePreviewBinding.SourceOf(attachment, previewUrls.Where(u => !usedUrls.Contains(u)));
            if (source is null)
            {
                LogWarning($"Hiding a link preview on message {message.Id} that matches no link in its text.");
                return true;
            }

            usedUrls.Add(source);
            if (InlinePreviewBinding.IsSpoiler(source))
                attachment.IsSpoiler = true;
            return false;
        });

        message.Attachments = kept;
    }

    /// <summary>
    /// Returns the verified record of a generation, loading it when needed.
    /// </summary>
    private async Task<ChannelKeyGenerationRecord> GetGenerationRecordAsync(Channel channel, int generation)
    {
        var ring = await GetKeyRingAsync(channel);
        if (ring is null)
            return null;
        if (!ring.Records.ContainsKey(generation))
            await EnsureRecordsAsync(channel, ring, generation, generation);
        return ring.Records.GetValueOrDefault(generation);
    }

    private static long ToUnixMs(DateTime time) =>
        new DateTimeOffset(time.Kind == DateTimeKind.Local
            ? time.ToUniversalTime()
            : DateTime.SpecifyKind(time, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    /// <summary>
    /// Keeps only the mentions the decrypted text contains. The server stores
    /// mentions beside the ciphertext, so a mention missing from the text is
    /// not highlighted or treated as a notification.
    /// </summary>
    internal static List<Mention> MentionsInText(List<Mention> mentions, string text)
    {
        if (mentions is null || mentions.Count == 0)
            return mentions;

        var inText = MentionTextParser.Parse(text ?? string.Empty);
        if (inText is null)
            return [];

        return mentions.Where(m => inText.Any(t => t.TargetId == m.TargetId && t.Type == m.Type)).ToList();
    }

    /// <summary>
    /// Returns an author's key state for verifying a signature by one of their
    /// devices. When the device is not known, the author's key log is loaded
    /// again, at most once per user every few seconds, because a device the
    /// author just added is not in a log loaded earlier. Unavailable is true
    /// when that could not be done now, so the message should be tried again
    /// later rather than rejected.
    /// </summary>
    private async Task<(UserKeyState State, bool Unavailable)> ResolveAuthorAsync(long userId, string deviceId,
        DateTime sentAt)
    {
        var state = await GetUserStateAsync(userId);
        if (state?.GetDeviceAt(deviceId, sentAt) is not null)
            return (state, false);

        var now = DateTime.UtcNow;
        if (_authorRefreshes.TryGetValue(userId, out var last) && now - last < AuthorRefreshInterval)
            return (state, true);
        _authorRefreshes[userId] = now;

        var (fresh, failed) = await FetchUserStateAsync(userId);
        return failed ? (state, true) : (fresh, false);
    }

    /// <summary>
    /// Keeps the signed text for reports and shows an escaped copy, since the
    /// server cannot apply its markdown safeguards to text it cannot read.
    /// </summary>
    private static void SetDecryptedContent(Message message, string content)
    {
        message.SignedContent = content;
        message.Content = MessageMarkdownSafety.Escape(content);
    }

    /// <summary>
    /// Shows decrypted embeds, each only if it passes the checks the server
    /// applies to embeds it can read, such as loading media only from allowed
    /// sources. A member's embed in a planet is also hidden when the member
    /// lacks the channel's Embed permission, which the server cannot check on
    /// encrypted messages. Server-sealed embeds were checked by the server.
    /// </summary>
    private async Task SetDecryptedEmbedsAsync(Message message, Channel channel, IReadOnlyList<string> embeds,
        bool checkAuthorPermission)
    {
        // Only embeds from the encrypted payload are shown, so decrypting the
        // same message again replaces them instead of adding copies.
        message.Attachments?.RemoveAll(a => a.Type == MessageAttachmentType.Embed);

        if (embeds is null || embeds.Count == 0)
            return;

        if (checkAuthorPermission && !await AuthorMayEmbedAsync(message, channel))
        {
            LogWarning($"Hiding the embed on message {message.Id}: its author lacks the Embed permission.");
            return;
        }

        var shown = 0;
        foreach (var embed in embeds)
        {
            var check = EmbedSafety.Check(embed);
            if (!check.Success)
            {
                LogWarning($"Hiding an embed on message {message.Id}: {check.Message}");
                continue;
            }

            // Embeds have no server ID, so each gets its own ID within the
            // message for the app to tell them apart when the message changes.
            var attachment = new MessageAttachment(MessageAttachmentType.Embed) { Id = -shown };
            attachment.SetEmbedPayload(embed);
            (message.Attachments ??= []).Add(attachment);
            shown++;
        }
    }

    /// <summary>
    /// Whether the author of a planet message may post embeds in its channel.
    /// When the author is no longer a member, or permissions cannot be loaded,
    /// the embed is shown, since the author was a member when sending.
    /// </summary>
    private async Task<bool> AuthorMayEmbedAsync(Message message, Channel channel)
    {
        if (channel.PlanetId is null || message.AuthorMemberId is null)
            return true;

        try
        {
            var author = await channel.Planet.FetchMemberAsync(message.AuthorMemberId.Value);
            return author is null ||
                   await channel.HasPermissionAsync(author, Valour.Shared.Authorization.ChatChannelPermissions.Embed);
        }
        catch (Exception e)
        {
            LogWarning($"Could not check the Embed permission for message {message.Id}: {e.Message}");
            return true;
        }
    }

    private static void MarkInvalid(Message message, string reason)
    {
        // Nothing about a message that failed its checks is shown, including
        // who it claims to mention.
        message.Content = string.Empty;
        message.Mentions = null;
        message.DecryptionState = MessageDecryptionState.Invalid;
        message.DecryptionError = reason;
    }

    /// <summary>
    /// Marks a message as waiting and remembers it so it is decrypted in place
    /// later: when its channel's key arrives, when the connection returns, or,
    /// with <paramref name="retryLater"/>, on a timer.
    /// </summary>
    private void MarkWaiting(Message message, string reason, bool retryLater = false, Channel channel = null)
    {
        message.DecryptionState = MessageDecryptionState.WaitingForKey;
        message.DecryptionError = reason;

        var key = ChannelKey(message.ChannelId, message.PlanetId);
        if (!_pendingMessages.ContainsKey(key) && _pendingMessages.Count >= MaxPendingChannels)
            return;

        var pending = _pendingMessages.GetOrAdd(key, _ => new ConcurrentDictionary<long, PendingMessage>());
        if (!pending.ContainsKey(message.Id) && pending.Count >= MaxPendingPerChannel)
            return;

        // A message received over realtime has no client yet, so its cache
        // scope comes from the node that serves its channel.
        var node = channel?.Node ?? (message.Client is null ? null : message.Node);
        pending[message.Id] = new PendingMessage
        {
            Reference = new WeakReference<Message>(message),
            Scope = node?.IsExternal == true ? node.Name : null,
            AuthorUserId = message.AuthorUserId,
            RetryLater = retryLater
        };

        if (retryLater)
            SchedulePendingRetry(message.ChannelId, message.PlanetId);
    }

    private void ForgetPending(Message message)
    {
        if (_pendingMessages.TryGetValue(ChannelKey(message.ChannelId, message.PlanetId), out var pending))
            pending.TryRemove(message.Id, out _);
    }

    /// <summary>
    /// Decrypts cached messages that were waiting for a channel's key.
    /// </summary>
    private async Task RetryPendingMessagesAsync(long channelId, long? planetId, Func<PendingMessage, bool> filter = null)
    {
        // New keys may also open sealed messages that could not be indexed.
        if (filter is null)
            ForgetSealedIndexFailures(channelId, planetId);

        var key = ChannelKey(channelId, planetId);
        if (!_pendingMessages.TryGetValue(key, out var pending))
            return;

        var decryptedAny = false;
        foreach (var (id, entry) in pending.ToArray())
        {
            if (filter is not null && !filter(entry))
                continue;

            // The cached instance is what the app shows, so that one is
            // decrypted. A message that is no longer cached or referenced has
            // nothing left to update.
            if (!_client.Cache.Messages.TryGet(id, entry.Scope, out var message) &&
                !entry.Reference.TryGetTarget(out message))
            {
                pending.TryRemove(id, out _);
                continue;
            }

            if (message.DecryptionState is MessageDecryptionState.Decrypted or MessageDecryptionState.Invalid)
            {
                pending.TryRemove(id, out _);
                continue;
            }

            await DecryptAsync(message);
            await Task.Yield();
            if (message.DecryptionState == MessageDecryptionState.WaitingForKey)
                continue;

            pending.TryRemove(id, out _);
            message.NotifyDecryptionChanged();
            if (message.DecryptionState == MessageDecryptionState.Decrypted)
            {
                decryptedAny = true;
                _client.MessageService.NotifyMessageDecrypted(message);
            }
        }

        if (pending.IsEmpty)
            _pendingMessages.TryRemove(key, out _);
        if (decryptedAny)
            _pendingRetryAttempts.TryRemove(key, out _);
    }

    /// <summary>
    /// Retries messages from one author, after their key log changed.
    /// </summary>
    private async Task RetryPendingMessagesFromAuthorAsync(long userId)
    {
        foreach (var (planetId, channelId) in _pendingMessages.Keys.ToArray())
            await RetryPendingMessagesAsync(channelId, planetId == 0 ? null : planetId,
                entry => entry.AuthorUserId == userId);
    }

    /// <summary>
    /// Retries every waiting message after the connection returned or the
    /// keys loaded. Key rings of those channels are loaded again, since key
    /// notices may have been missed.
    /// </summary>
    private async Task ResyncPendingMessagesAsync()
    {
        foreach (var (planetId, channelId) in _pendingMessages.Keys.ToArray())
        {
            _keyRings.TryRemove((planetId, channelId), out _);
            _lastKeyRequests.TryRemove((planetId, channelId), out _);
            await RetryPendingMessagesAsync(channelId, planetId == 0 ? null : planetId);
        }
    }

    /// <summary>
    /// Retries a channel's waiting messages after a delay that grows while
    /// retries find nothing new, from 15 seconds up to 5 minutes.
    /// </summary>
    private void SchedulePendingRetry(long channelId, long? planetId)
    {
        var key = ChannelKey(channelId, planetId);
        if (!_scheduledPendingRetries.TryAdd(key, 0))
            return;

        var attempts = _pendingRetryAttempts.AddOrUpdate(key, 0, (_, current) => Math.Min(current + 1, 5));
        var delay = TimeSpan.FromTicks(FirstPendingRetry.Ticks << attempts);
        if (delay > LongestPendingRetry)
            delay = LongestPendingRetry;

        var token = _session.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                _scheduledPendingRetries.TryRemove(key, out _);
                await RetryPendingMessagesAsync(channelId, planetId, entry => entry.RetryLater);
            }
            catch (OperationCanceledException)
            {
                // Logged out.
            }
            catch (Exception e)
            {
                LogError($"Failed to retry waiting messages in channel {channelId}", e);
            }
            finally
            {
                _scheduledPendingRetries.TryRemove(key, out _);
            }
        });
    }

    /// <summary>
    /// Re-runs decryption for cached messages that could not be read before
    /// this device was verified.
    /// </summary>
    public async Task RetryUnverifiedMessagesAsync(IEnumerable<Message> messages)
    {
        foreach (var message in messages.Where(m => m.IsEncrypted && m.DecryptionState != MessageDecryptionState.Decrypted))
        {
            await DecryptAsync(message);
            message.NotifyDecryptionChanged();
            if (message.DecryptionState == MessageDecryptionState.Decrypted)
                _client.MessageService.NotifyMessageDecrypted(message);
            await Task.Yield();
        }
    }

    /// <summary>
    /// Clears per-account message state when logging out.
    /// </summary>
    private void ResetMessageState()
    {
        _pendingMessages.Clear();
        _pendingRetryAttempts.Clear();
        _scheduledPendingRetries.Clear();
        _authorRefreshes.Clear();
        _sealedIndexing.Clear();
    }

    /// <summary>
    /// Keeps caches that grow with the number of people and messages seen
    /// from growing without bound in long-running apps and bots.
    /// </summary>
    private void TrimCaches()
    {
        if (_userStates.Count > MaxUserStates)
        {
            foreach (var userId in _userStates.OrderBy(e => e.Value.FetchedAt).Take(_userStates.Count - MaxUserStates / 2)
                         .Select(e => e.Key).ToList())
                _userStates.TryRemove(userId, out _);
        }

        if (_authorRefreshes.Count > MaxUserStates)
        {
            var cutoff = DateTime.UtcNow - AuthorRefreshInterval;
            foreach (var (userId, at) in _authorRefreshes.ToArray())
            {
                if (at < cutoff)
                    _authorRefreshes.TryRemove(userId, out _);
            }
        }

        foreach (var (key, pending) in _pendingMessages.ToArray())
        {
            foreach (var (id, entry) in pending.ToArray())
            {
                if (!entry.Reference.TryGetTarget(out _) && !_client.Cache.Messages.TryGet(id, entry.Scope, out _))
                    pending.TryRemove(id, out _);
            }

            if (pending.IsEmpty)
                _pendingMessages.TryRemove(key, out _);
        }
    }

    // Search

    /// <summary>
    /// Searches an encrypted channel. The server matches keyed terms without
    /// learning the words; results are decrypted and checked here.
    /// </summary>
    public async Task<List<Message>> SearchAsync(Channel channel, string query, int count = 25)
    {
        var ring = await GetKeyRingAsync(channel);
        if (ring is null || string.IsNullOrWhiteSpace(query))
            return [];

        // Each search key the channel used produces different terms, so the
        // query is hashed with every one this device can unlock, once for
        // messages members encrypted and once for messages the server sealed.
        // The sets are shuffled so the server cannot tell which is which.
        var termSets = new List<int[]>();
        foreach (var indexGeneration in ring.IndexGenerations.Take(E2eeLimits.MaxSearchIndexGenerations))
        {
            var indexSecret = await GetSecretAsync(channel, indexGeneration);
            if (indexSecret is null)
                continue;

            foreach (var key in new[] { indexSecret.IndexKey, indexSecret.SealedIndexKey })
            {
                // Every term must match, so dropping some of a long query's
                // terms still finds every match; decrypted results are checked.
                var terms = SearchTerms.ForQuery(key, channel.Id, query);
                if (terms.Length > 0)
                    termSets.Add(terms.Take(E2eeLimits.MaxTermsPerSearchSet).ToArray());
            }
        }

        if (termSets.Count == 0)
            return [];

        var shuffled = termSets.ToArray();
        Random.Shared.Shuffle(shuffled);

        var result = await channel.Node.PostAsyncWithResponse<List<Message>>(ChannelRoute(channel, "search"),
            new EncryptedSearchRequest { TermSets = shuffled.ToList(), Count = Math.Clamp(count * 2, 1, E2eeLimits.MaxSearchResults) });
        if (!result.Success || result.Data is null)
            return [];

        await DecryptAllAsync(result.Data);
        return result.Data
            .Where(m => m.DecryptionState == MessageDecryptionState.Decrypted && SearchTerms.Matches(m.Content, query))
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Channels whose server-sealed history this device indexes, with the
    /// messages it could not decrypt and when to look again.
    /// </summary>
    private sealed class SealedIndexState
    {
        public HashSet<long> Failed { get; } = new();
        public DateTime NextAttempt { get; set; }
        public int EmptyRuns { get; set; }
    }

    private readonly ConcurrentDictionary<(long PlanetId, long ChannelId), SealedIndexState> _sealedIndexing = new();
    private static readonly TimeSpan SealedIndexInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LongestSealedIndexInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Computes search terms for messages the server sealed, which it could
    /// not index itself. Runs a bounded number of batches per call. A channel
    /// is checked again only after a while, and messages this device could
    /// not decrypt are not decrypted again until the channel's keys change.
    ///
    /// The server chooses the text of sealed messages, so their terms use the
    /// generation's <see cref="ChannelKeySecret.SealedIndexKey"/>. Hashing
    /// them with the key for members' messages would let the server learn the
    /// terms of any word by sealing it.
    /// </summary>
    public async Task IndexSealedHistoryAsync(Channel channel, int maxBatches = 5)
    {
        if (Status != E2eeStatus.Ready || !channel.IsEncrypted)
            return;

        var state = _sealedIndexing.GetOrAdd(ChannelKey(channel), _ => new SealedIndexState());
        if (DateTime.UtcNow < state.NextAttempt)
            return;
        state.NextAttempt = DateTime.UtcNow + SealedIndexInterval;

        for (var batch = 0; batch < maxBatches; batch++)
        {
            var result = await channel.Node.GetJsonAsync<List<Message>>(
                ChannelRouteWith(channel, "unindexed") + $"count={E2eeLimits.MaxUnindexedBatch}",
                cacheDurationMs: null);
            if (!result.Success || result.Data is null || result.Data.Count == 0)
                return;

            var uploads = new List<MessageTermsDto>();
            foreach (var message in result.Data)
            {
                lock (state.Failed)
                {
                    if (state.Failed.Contains(message.Id))
                        continue;
                }

                // Only sealed messages of this channel are indexed here.
                var sealedHere = message.EncryptionVersion == MessageEncryption.ServerSealed &&
                                 message.ChannelId == channel.Id && message.PlanetId == channel.PlanetId;
                if (sealedHere)
                {
                    await DecryptAsync(message);

                    // A browser runs this on its only thread. Task.Yield can
                    // resume without returning to the browser, so a timer is
                    // used to let rendering and input run between messages.
                    await Task.Delay(1);
                }

                var indexSecret = sealedHere && message.DecryptionState == MessageDecryptionState.Decrypted
                    ? await GetIndexSecretAsync(channel, message.KeyGeneration)
                    : null;
                if (indexSecret is null)
                {
                    lock (state.Failed)
                        state.Failed.Add(message.Id);
                    continue;
                }

                uploads.Add(new MessageTermsDto
                {
                    MessageId = message.Id,
                    Terms = SearchTerms.ForMessage(indexSecret.SealedIndexKey, channel.Id, message.SignedContent)
                });
            }

            if (uploads.Count == 0)
            {
                // Only messages this device cannot read are left in the
                // newest batch, so it waits longer before looking again.
                state.EmptyRuns++;
                var wait = TimeSpan.FromTicks(SealedIndexInterval.Ticks << Math.Min(state.EmptyRuns, 6));
                state.NextAttempt = DateTime.UtcNow + (wait > LongestSealedIndexInterval ? LongestSealedIndexInterval : wait);
                return;
            }

            state.EmptyRuns = 0;
            var upload = await channel.Node.PostAsync(ChannelRoute(channel, "terms"), uploads);
            if (!upload.Success || result.Data.Count < E2eeLimits.MaxUnindexedBatch)
                return;
        }
    }

    /// <summary>
    /// Lets sealed messages this device could not decrypt be tried again
    /// once a channel's keys change.
    /// </summary>
    private void ForgetSealedIndexFailures(long channelId, long? planetId) =>
        _sealedIndexing.TryRemove(ChannelKey(channelId, planetId), out _);
    // Reports

    /// <summary>
    /// Builds the evidence a reporter reveals for a message. Revealing it
    /// lets moderators read the message and lets the server prove the author
    /// sent it; nothing else from the channel is revealed.
    /// </summary>
    public static MessageEvidenceDto BuildEvidence(Message message)
    {
        if (message is null)
            return null;

        return new MessageEvidenceDto
        {
            MessageId = message.Id,
            Revision = message.EncryptionVersion == MessageEncryption.EndToEnd ? message.EncryptedRevision : 0,
            Content = message.SignedContent ?? message.Content,
            Embed = message.IsEncrypted ? EvidenceEmbed(message) : null,
            FrankingKey = message.FrankingKey,
            Salt = message.SealedSalt
        };
    }

    /// <summary>
    /// The embed value the author's signature or the server's attestation
    /// covers, taken from the decrypted payload rather than the embeds shown,
    /// which may have been hidden by safety checks or changed by live updates.
    /// A server-sealed message can hold several embeds.
    /// </summary>
    private static string EvidenceEmbed(Message message) =>
        message.EncryptionVersion == MessageEncryption.ServerSealed
            ? ServerSealing.AttestedEmbed(message.SignedEmbeds)
            : message.SignedEmbeds is { Count: > 0 } embeds ? embeds[0] : null;
}

/// <summary>
/// Finds the links in message text that the sender asks the server to
/// preview, since the server cannot read encrypted text. It follows the rules
/// the server applies to text it can read: a link written as &lt;url&gt; is
/// not previewed, links in code are not previewed, and a link inside a
/// ||spoiler|| is sent as ||url|| so its preview is hidden as a spoiler too.
/// The part of a link after '#' is never sent, because it can hold a secret,
/// such as the key in an encrypted invite link.
/// </summary>
internal static class MessagePreviewUrls
{
    public const int MaxUrls = E2eeLimits.MaxPreviewUrls;

    public static List<string> Extract(string content)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(content))
            return urls;

        var code = CodeRanges(content);
        foreach (Match match in Valour.Shared.Cdn.CdnUtils.UrlRegex.Matches(content))
        {
            // An image written as markdown, ![](url), was never previewed.
            if (match.Value.StartsWith("![](", StringComparison.Ordinal))
                continue;
            if (IsInside(code, match.Index) || IsBracketed(content, match))
                continue;

            var url = match.Value.TrimEnd(',', '\'', '?');
            if (url.EndsWith(')') && url.Count(c => c == ')') > url.Count(c => c == '('))
                url = url[..^1];

            // The fragment never reaches a web server, and it can hold a
            // secret, such as the key in an encrypted invite link, so the
            // preview server never sees it.
            var fragment = url.IndexOf('#');
            if (fragment >= 0)
                url = url[..fragment];
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                continue;

            if (IsInsideSpoiler(content, match.Index, code))
                url = $"||{url}||";
            if (urls.Contains(url))
                continue;

            urls.Add(url);
            if (urls.Count == MaxUrls)
                break;
        }

        return urls;
    }

    private static bool IsBracketed(string content, Match match)
    {
        var start = match.Index - 1;
        var end = match.Index + match.Length;
        return start >= 0 && end < content.Length && content[start] == '<' && content[end] == '>';
    }

    /// <summary>
    /// True when the index is inside a ||spoiler||, counting the markers
    /// before it that are not in code.
    /// </summary>
    private static bool IsInsideSpoiler(string content, int index, List<(int Start, int End)> code)
    {
        var markers = 0;
        var i = 0;
        while (i < index - 1)
        {
            if (content[i] == '|' && content[i + 1] == '|' && !IsInside(code, i))
            {
                markers++;
                i += 2;
            }
            else
            {
                i++;
            }
        }

        return markers % 2 == 1;
    }

    private static bool IsInside(List<(int Start, int End)> ranges, int index) =>
        ranges.Any(r => index >= r.Start && index < r.End);

    /// <summary>
    /// The ranges of code blocks (```) and inline code (a run of backticks
    /// closed by a run of the same length).
    /// </summary>
    private static List<(int Start, int End)> CodeRanges(string content)
    {
        var ranges = new List<(int, int)>();
        var i = 0;
        while (i < content.Length)
        {
            if (content[i] != '`')
            {
                i++;
                continue;
            }

            var run = 0;
            while (i + run < content.Length && content[i + run] == '`')
                run++;

            var fence = new string('`', run);
            var close = content.IndexOf(fence, i + run, StringComparison.Ordinal);
            while (close >= 0 && close + run < content.Length && content[close + run] == '`')
                close = content.IndexOf(fence, close + run + 1, StringComparison.Ordinal);

            if (close < 0)
            {
                i += run;
                continue;
            }

            ranges.Add((i, close + run));
            i = close + run;
        }

        return ranges;
    }
}
