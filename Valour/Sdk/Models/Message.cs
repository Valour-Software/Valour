using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models.Embeds;
using Valour.Shared.Models;
using Valour.Shared;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Models;

public class Message : ClientPlanetModel<Message, long>, ISharedMessage
{
    /// <summary>
    /// Run when a reaction is added to this message
    /// </summary>
    public HybridEvent<MessageReaction> ReactionAdded;
    
    /// <summary>
    /// Run when a reaction is removed from this message
    /// </summary>
    public HybridEvent<MessageReaction> ReactionRemoved;
    
    public override string BaseRoute =>
        $"api/messages";
    
    /// <summary>
    /// The planet (if any) this message belongs to
    /// </summary>
    public long? PlanetId { get; set; }
    protected override long? GetPlanetId() => PlanetId;

    /// <summary>
    /// The message (if any) this is a reply to
    /// </summary>
    public long? ReplyToId { get; set; }

    /// <summary>
    /// The author's user ID
    /// </summary>
    public long AuthorUserId { get; set; }
    
    /// <summary>
    /// The author's planet member id (if any)
    /// </summary>
    public long? AuthorMemberId { get; set; }

    /// <summary>
    /// String representation of message
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// The time the message was sent (in UTC)
    /// </summary>
    public DateTime TimeSent { get; set; }

    /// <summary>
    /// Id of the channel this message belonged to
    /// </summary>
    public long ChannelId { get; set; }
    
    /// <summary>
    /// Used to identify a message returned from the server
    /// </summary>
    public string Fingerprint { get; set; }

    /// <summary>
    /// The time when the message was edited, or null if it was not
    /// </summary>
    public DateTime? EditedTime { get; set; }

    /// <summary>
    /// Server-managed provenance for content imported from another service.
    /// </summary>
    public string ImportSource { get; set; }

    /// <summary>
    /// Non-null when this message was sent through a webhook. Server-managed.
    /// </summary>
    public long? WebhookId { get; set; }

    /// <summary>
    /// Display-name override for webhook messages. Server-managed.
    /// </summary>
    public string OverrideName { get; set; }

    /// <summary>
    /// Immutable Valour CDN avatar asset used by this webhook message.
    /// </summary>
    public long? WebhookAvatarAssetId { get; set; }

    public bool WebhookAvatarAnimated { get; set; }

    /// <summary>
    /// How the text is stored. See <see cref="MessageEncryption"/>. The SDK
    /// decrypts encrypted messages before they reach the cache, so
    /// <see cref="Content"/> holds the readable text when decryption succeeds.
    /// </summary>
    public int EncryptionVersion { get; set; }

    /// <summary>
    /// The encrypted message, when <see cref="EncryptionVersion"/> is nonzero.
    /// </summary>
    public byte[] Envelope { get; set; }

    /// <summary>
    /// The channel key generation the envelope was encrypted with.
    /// </summary>
    public int KeyGeneration { get; set; }

    /// <summary>
    /// Keyed search terms sent with an encrypted message. Request-only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int[] SearchTerms { get; set; }

    /// <summary>
    /// Links in an encrypted message that the sender wants previewed.
    /// Request-only; the server cannot read the text to find them.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> PreviewUrls { get; set; }

    /// <summary>
    /// IDs of the custom planet emojis in an encrypted message's text, so the
    /// server can check them. Request-only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<long> CustomEmojiIds { get; set; }

    /// <summary>
    /// Whether the text of an encrypted message could be read.
    /// </summary>
    [JsonIgnore]
    public MessageDecryptionState DecryptionState { get; set; }

    /// <summary>
    /// Why an encrypted message could not be read, when it could not.
    /// </summary>
    [JsonIgnore]
    public string DecryptionError { get; set; }

    /// <summary>
    /// The revision of an end-to-end encrypted message. Edits increase it.
    /// </summary>
    [JsonIgnore]
    public int EncryptedRevision { get; set; }

    /// <summary>
    /// The key that opens this message's franking commitment. Revealing it
    /// with the text in a report proves the author sent that text.
    /// </summary>
    [JsonIgnore]
    public byte[] FrankingKey { get; set; }

    /// <summary>
    /// The decrypted text exactly as its author signed it. <see cref="Content"/>
    /// holds the display copy, which escapes unsafe markdown as the server does
    /// for plain text. Reports reveal this copy so the commitment verifies.
    /// </summary>
    [JsonIgnore]
    public string SignedContent { get; set; }

    /// <summary>
    /// For messages the server sealed, the salt that proves the text against
    /// the server's attestation.
    /// </summary>
    [JsonIgnore]
    public byte[] SealedSalt { get; set; }

    /// <summary>
    /// For messages the server sealed, why it sealed them. The server knew the
    /// text of these messages. <see cref="ServerSealedKind.Legacy"/> messages
    /// were written before the channel was encrypted, and apps label them so
    /// people can tell them apart from messages encrypted by their author.
    /// </summary>
    [JsonIgnore]
    public ServerSealedKind? SealedKind { get; set; }

    /// <summary>
    /// True for a message written before its channel was encrypted, which the
    /// server sealed later.
    /// </summary>
    [JsonIgnore]
    public bool IsLegacySealed => SealedKind == ServerSealedKind.Legacy;

    /// <summary>
    /// The embeds of an encrypted message exactly as its author signed them or
    /// the server attested them. The embeds shown may differ, because unsafe
    /// ones are hidden and live updates change them; reports reveal these.
    /// </summary>
    [JsonIgnore]
    internal IReadOnlyList<string> SignedEmbeds { get; set; }

    /// <summary>
    /// True when the server returned files with an end-to-end encrypted
    /// message that its author did not send. They are not shown, and apps
    /// tell the person that some attachments were withheld.
    /// </summary>
    [JsonIgnore]
    public bool AttachmentsWithheld { get; internal set; }

    /// <summary>
    /// True when the message's text is encrypted on the server.
    /// </summary>
    [JsonIgnore]
    public bool IsEncrypted => EncryptionVersion != MessageEncryption.None;

    /// <summary>
    /// Raised when an encrypted message is decrypted after it was first shown,
    /// for example once a missing key arrives.
    /// </summary>
    [JsonIgnore]
    public HybridEvent<Message> DecryptionChanged;

    public async ValueTask<IMessageAuthor> FetchAuthorAsync()
    {
        if (AuthorMemberId is not null)
            return await FetchAuthorMemberAsync();
        
        return await FetchAuthorUserAsync();
    }
    
    /// <summary>
    /// If we are replying to a message, this is the message we are replying to
    /// </summary>
    public Message ReplyTo { get; set; }
    
    /// <summary>
    /// Reactions to this message
    /// </summary>
    public List<MessageReaction> Reactions { get; set; }
    
    /// <summary>
    /// Attachments on this message
    /// </summary>
    public List<MessageAttachment> Attachments { get; set; }

    /// <summary>
    /// The mentions contained within this message
    /// </summary>
    public List<Mention> Mentions { get; set; }
    
    // Prevents wasting time repeatedly grabbing the same data
    #region Cached Props
    
    // Cached author user
    private User _authorUserCached;
    
    // Cached author member
    private PlanetMember _authorMemberCached;
    
    #endregion
    
    [JsonConstructor]
    private Message() : base() {}
    public Message(ValourClient client) : base(client)
    {
        
    }

    public Message(string content, long? planetId, long? memberId, long userId, long channelId, ValourClient client)
        : base(client)
    {
        Content = content;
        PlanetId = planetId;
        AuthorMemberId = memberId;
        AuthorUserId = userId;
        ChannelId = channelId;
        TimeSent = DateTime.UtcNow;
        Fingerprint = Guid.NewGuid().ToString();
    }
    
    public void NotifyReactionAdded(MessageReaction reaction)
    {
        Reactions ??= new List<MessageReaction>();
        Reactions.Add(reaction);

        ReactionAdded?.Invoke(reaction);
    }

    public void NotifyReactionRemoved(MessageReaction reaction)
    {
        Reactions?.RemoveAll(x =>
            x.Emoji == reaction.Emoji && x.AuthorUserId == reaction.AuthorUserId);

        ReactionRemoved?.Invoke(reaction);
    }
    
    /// <summary>
    /// Returns the message this was a reply to (if any)
    /// </summary>
    public async ValueTask<Message> FetchReplyMessageAsync()
    {
        if (ReplyTo is not null)
            return ReplyTo;

        if (ReplyToId is null)
            return null;
        
        return await Client.MessageService.FetchMessageAsync(ReplyToId.Value, Planet);
    }

    public async Task<TaskResult<UserChannelState>> MarkUnreadAsync()
    {
        var result = await Node.PostAsyncWithResponse<UserChannelState>($"{IdRoute}/mark-unread", null);

        if (result.Success)
        {
            Client.UnreadService.MarkChannelUnread(PlanetId, ChannelId);
            Client.ChannelStateService.OnUserChannelStateUpdated(result.Data);
        }
        else
        {
            Client.Logger.Log("Message", "Failed to mark message unread: " + result.Message, "yellow");
        }

        return result;
    }

    public ValueTask<Channel> FetchChannelAsync()
    {
        if (PlanetId is null)
            return Client.ChannelService.FetchDirectChannelAsync(ChannelId);
        else
            return Planet.FetchChannelAsync(ChannelId);
    }

    /// <summary>
    /// Returns the user for the author of this message
    /// </summary>
    public async ValueTask<User> FetchAuthorUserAsync(bool skipCache = false)
    {
        _authorUserCached ??= await Client.UserService.FetchUserAsync(AuthorUserId, skipCache);
        return _authorUserCached;
    }
    
    /// <summary>
    /// Returns the planet member for the author of this message (if any)
    /// </summary>
    public async ValueTask<PlanetMember> FetchAuthorMemberAsync(bool skipCache = false)
    {
        if (_authorMemberCached is not null)
            return _authorMemberCached;

        if (AuthorMemberId is null)
            return null;
        
        _authorMemberCached = await Planet.FetchMemberAsync(AuthorMemberId.Value, skipCache);
        if (_authorMemberCached is not null) // Also set user :)
            _authorUserCached = _authorMemberCached.User;
        
        return _authorMemberCached;
    }
    
    public bool CheckIfMentioned()
    {
        if (Mentions is null || Mentions.Count == 0)
            return false;

        foreach (var mention in Mentions)
        {
            switch (mention.Type)
            {
                case MentionType.User:
                {
                    if (mention.TargetId == Client.Me.Id)
                    {
                        return true;
                    }

                    break;
                }
                case MentionType.PlanetMember:
                {
                    if (PlanetId is null)
                        continue;
                    
                    if (mention.TargetId == Planet.MyMember?.Id)
                        return true;
                    
                    break;
                }
                case MentionType.Role:
                {
                    if (PlanetId is null || Planet.MyMember is null)
                        continue;
                    
                    if (Planet.MyMember.Roles.Any(x => x.Id == mention.TargetId))
                        return true;

                    break;
                }
                default:
                    continue;
            }
        }

        return false;
    }

    public void SetupMentionsList()
    {
        Mentions ??= new();
    }

    [JsonIgnore]
    public MessageAttachment EmbedAttachment =>
        Attachments?.FirstOrDefault(x => x.Type == MessageAttachmentType.Embed);

    [JsonIgnore]
    public Embed Embed => EmbedAttachment?.Embed;

    public void SetMentions(List<Mention> mentions)
    {
        Mentions = mentions;
    }

    public void SetAttachments(List<MessageAttachment> attachments)
    {
        Attachments = attachments;
    }

    public void ClearMentions()
    {
        Mentions = null;
    }

    /// <summary>
    /// Returns true if the message is a embed
    /// </summary>
    public bool IsEmbed()
    {
        return EmbedAttachment is not null;
    }

    public void SetEmbedParsed(bool val)
    {
        EmbedAttachment?.SetEmbedParsed(val);
    }

    public void SetEmbed(Embed embed)
    {
        if (embed is null)
        {
            Attachments?.RemoveAll(x => x.Type == MessageAttachmentType.Embed);
            return;
        }

        var attachment = EmbedAttachment;
        if (attachment is null)
        {
            Attachments ??= [];
            Attachments.Add(MessageAttachment.CreateEmbed(embed));
        }
        else
        {
            attachment.SetEmbed(embed);
        }
    }

    public void SetEmbedPayload(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            Attachments?.RemoveAll(x => x.Type == MessageAttachmentType.Embed);
            return;
        }

        var attachment = EmbedAttachment;
        if (attachment is null)
        {
            Attachments ??= [];
            attachment = new MessageAttachment(MessageAttachmentType.Embed);
            Attachments.Add(attachment);
        }

        attachment.SetEmbedPayload(data);
    }
    
    /// <summary>
    /// Sends a live update to this message's embed. Only the bot that sent
    /// the message can do this. The update is encrypted with the channel key.
    /// </summary>
    public Task<TaskResult> SendEmbedUpdateAsync(EmbedUpdate update) =>
        Client.E2eeService.SendEmbedUpdateAsync(this, update);

    public bool IsEmpty()
    {
        // An encrypted message this device cannot read yet has no text, but
        // it is still a message; apps show why it cannot be read.
        if (IsEncrypted)
            return false;

        // early returns are faster than checking all conditions
        if (!string.IsNullOrWhiteSpace(Content))
            return false;
        
        if (Attachments is not null && Attachments.Count > 0)
            return false;
        
        return ReplyToId is null;
    }
    
    public Task AddReactionAsync(string emoji)
    {
        return Client.MessageService.AddMessageReactionAsync(this.Id, emoji);
    }
    
    public Task RemoveReactionAsync(string emoji)
    {
        return Client.MessageService.RemoveMessageReactionAsync(this.Id, emoji);
    }
    
    public void Clear()
    {
        Content = null;
        Attachments = null;
        Mentions = null;
    }
    
    // Alias for CreateAsync
    public Task<TaskResult<Message>> PostAsync() => 
        CreateAsync();

    /// <summary>
    /// Sends this message. The text is end-to-end encrypted on this device
    /// first and never sent in the clear.
    /// </summary>
    public override Task<TaskResult<Message>> CreateAsync() =>
        Client.E2eeService.SendMessageAsync(this);

    /// <summary>
    /// Saves an edit. The new text is encrypted as the message's next revision.
    /// </summary>
    public override Task<TaskResult<Message>> UpdateAsync() =>
        Client.E2eeService.EditMessageAsync(this);

    /// <summary>
    /// Creates a copy to send to the server with the given encrypted fields,
    /// leaving this instance, which the UI may be showing, unchanged.
    /// </summary>
    internal Message CreateWireCopy()
    {
        var copy = (Message)MemberwiseClone();
        copy.ReplyTo = null;
        copy.ReactionAdded = null;
        copy.ReactionRemoved = null;
        copy.DecryptionChanged = null;
        copy.Attachments = Attachments?.ToList();
        copy.Mentions = Mentions?.ToList();
        return copy;
    }

    internal void NotifyDecryptionChanged() => DecryptionChanged?.Invoke(this);

    public override Message AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        if (IsEncrypted && Client.Cache.Messages.TryGet(Id, CacheScope, out var cached))
        {
            // Realtime events can arrive out of order. A copy of an earlier
            // revision must not replace an edit the cache already holds.
            if (EncryptionVersion == MessageEncryption.EndToEnd &&
                cached.EncryptionVersion == MessageEncryption.EndToEnd &&
                cached.DecryptionState == MessageDecryptionState.Decrypted &&
                DecryptionState == MessageDecryptionState.Decrypted &&
                cached.EncryptedRevision > EncryptedRevision)
                return cached;

            // An encrypted copy that was not decrypted must not overwrite text
            // the cache already decrypted from the same envelope.
            if (DecryptionState == MessageDecryptionState.NotAttempted &&
                cached.DecryptionState != MessageDecryptionState.NotAttempted &&
                cached.Envelope is not null && Envelope is not null && cached.Envelope.AsSpan().SequenceEqual(Envelope))
            {
                CopyDecryptionFrom(cached);
            }
        }

        return Client.Cache.Messages.Put(this, flags, CacheScope);
    }

    internal void CopyDecryptionFrom(Message source)
    {
        Content = source.Content;
        DecryptionState = source.DecryptionState;
        DecryptionError = source.DecryptionError;
        EncryptedRevision = source.EncryptedRevision;
        FrankingKey = source.FrankingKey;
        SignedContent = source.SignedContent;
        SealedSalt = source.SealedSalt;
        SealedKind = source.SealedKind;
        SignedEmbeds = source.SignedEmbeds;
        AttachmentsWithheld = source.AttachmentsWithheld;
        Mentions = source.Mentions;

        // Embeds on an encrypted message come only from its decrypted payload,
        // and files only from the list its author signed, never from what the
        // server returned beside the ciphertext, so the checked list is copied.
        Attachments = source.Attachments?.ToList();
    }

    public override Message RemoveFromCache(bool skipEvents = false)
    {
        return Client.Cache.Messages.Remove(this, CacheScope, skipEvents);
    }
    
    public override void SyncSubModels(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        ReplyTo = ReplyTo?.Sync(Client, flags);
    }
}
