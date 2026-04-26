using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.Models.Messages.Embeds;
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
        ReactionAdded?.Invoke(reaction);
    }
    
    public void NotifyReactionRemoved(MessageReaction reaction)
    {
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
    
    public bool IsEmpty()
    {
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

    public override Message AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Client.Cache.Messages.Put(this, flags);
    }

    public override Message RemoveFromCache(bool skipEvents = false)
    {
        return Client.Cache.Messages.Remove(this, skipEvents);
    }
    
    public override void SyncSubModels(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        ReplyTo = ReplyTo?.Sync(Client, flags);
    }
}
