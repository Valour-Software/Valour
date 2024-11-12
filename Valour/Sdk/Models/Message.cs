using System.Text.Json;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Shared.Models;
using Valour.Shared;
using Valour.Sdk.ModelLogic;

namespace Valour.Sdk.Models;

public class Message : ClientPlanetModel<Message, long>, ISharedMessage
{
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
    /// Data for representing an embed
    /// </summary>
    public string EmbedData { get; set; }

    /// <summary>
    /// Data for representing mentions in a message
    /// </summary>
    public string MentionsData { get; set; }

    /// <summary>
    /// Data for representing attachments in a message
    /// </summary>
    public string AttachmentsData { get; set; }

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

    private string _renderKey;

    public string RenderKey
    {
        get => _renderKey ??= Guid.NewGuid().ToString();
        set => _renderKey = value;
    }
    
    /// <summary>
    /// If we are replying to a message, this is the message we are replying to
    /// </summary>
    public Message ReplyTo { get; set; }
    
    #region Generation
    
    /////////////////////////////////
    // Advanced message data below //
    /////////////////////////////////

    /// <summary>
    /// The mentions contained within this message
    /// </summary>
    private List<Mention> _mentions;

    /// <summary>
    /// True if the mentions data has been parsed
    /// </summary>
    private bool _mentionsParsed;

    /// <summary>
    /// The inner embed data
    /// </summary>
    private Embed _embed;

    /// <summary>
    /// True if the embed data has been parsed
    /// </summary>
    private bool _embedParsed;

    /// <summary>
    /// The inner attachments data
    /// </summary>
    private List<MessageAttachment> _attachments;
    
    /// <summary>
    /// True if the attachments have been parsed
    /// </summary>
    private bool _attachmentsParsed;
    
    #endregion
    
    // Prevents wasting time repeatedly grabbing the same data
    #region Cached Props
    
    // Cached author user
    private User _authorUserCached;
    
    // Cached author member
    private PlanetMember _authorMemberCached;
    
    #endregion
    
    public Message()
    {
        
    }

    public Message(string content, long? planetId, long? memberId, long userId, long channelId)
    {
        Content = content;
        PlanetId = planetId;
        AuthorMemberId = memberId;
        AuthorUserId = userId;
        ChannelId = channelId;
        TimeSent = DateTime.UtcNow;
        Fingerprint = Guid.NewGuid().ToString();
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
            return Client.ChannelService.FetchChannelAsync(ChannelId);
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
        
        PlanetMember selfMember = null;

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
                    
                    if (Planet.MyMember.Roles.ContainsId(mention.TargetId))
                        return true;

                    break;
                }
                default:
                    continue;
            }
        }

        return false;
    }

    /// <summary>
    /// The mentions within this message
    /// </summary>
    public List<Mention> Mentions
    {
        get
        {
            if (!_mentionsParsed)
            {
                if (!string.IsNullOrEmpty(MentionsData))
                {
                    _mentions = JsonSerializer.Deserialize<List<Mention>>(MentionsData);
                }

                _mentionsParsed = true;
            }

            return _mentions;
        }
    }

    public void SetupMentionsList()
    {
        if (_mentions is null)
            _mentions = new();
    }

    public Embed Embed
    {
        get
        {
            if (!_embedParsed)
            {
                if (!string.IsNullOrEmpty(EmbedData))
                {
                    //Console.WriteLine(EmbedData);
                    // prevent a million errors in console
                    if (EmbedData.Contains("EmbedVersion\":\"1.1.0\""))
                    {
                        _embedParsed = true;
                        return null;
                    }
                    _embed = JsonSerializer.Deserialize<Embed>(EmbedData);
                    if (_embed.Pages is not null)
                    {
                        foreach (var page in _embed.Pages)
                        {
                            if (page.Children is not null)
                            {
                                foreach (var item in page.Children)
                                {
                                    item.Embed = _embed;
                                    item.Init(_embed, page);
                                }
                            }
                        }
                    }
                }

                _embedParsed = true;
            }

            return _embed;
        }
    }

    public List<MessageAttachment> Attachments
    {
        get
        {
            if (!_attachmentsParsed)
            {
                if (!string.IsNullOrEmpty(AttachmentsData))
                {
                    _attachments = JsonSerializer.Deserialize<List<MessageAttachment>>(AttachmentsData);
                }

                _attachmentsParsed = true;
            }

            return _attachments;
        }
    }

    public void SetMentions(List<Mention> mentions)
    {
        _mentions = mentions;
        
        if (mentions is null || mentions.Count == 0)
        {
            MentionsData = null;
        }
        else
        {
            MentionsData = JsonSerializer.Serialize(mentions);
        }
    }

    public void SetAttachments(List<MessageAttachment> attachments)
    {
        _attachments = attachments;
        
        if (_attachments is null || _attachments.Count == 0)
        {
            AttachmentsData = null;
        }
        else
        {
            AttachmentsData = JsonSerializer.Serialize(attachments);
        }
    }

    public void ClearMentions()
    {
        MentionsData = null;
        _mentions = null;
    }

    /// <summary>
    /// Returns true if the message is a embed
    /// </summary>
    public bool IsEmbed()
    {
        return !string.IsNullOrWhiteSpace(EmbedData);
    }
    
    public void SetEmbedParsed(bool val)
    {
        _embedParsed = val;
    }
    
    public bool IsEmpty()
    {
        // early returns are faster than checking all conditions
        if (!string.IsNullOrEmpty(Content))
            return false;
        
        if (!string.IsNullOrWhiteSpace(EmbedData))
            return false;
        
        if (!string.IsNullOrWhiteSpace(AttachmentsData))
            return false;
        
        if (_attachments is not null && _attachments.Count > 0)
            return false;
        
        return ReplyToId is null;
    }
    
    public void Clear()
    {
        MentionsData = null;
        AttachmentsData = null;
        EmbedData = null;
        Content = null;
        
        _mentions = null;
        _attachments = null;
        _embed = null;

        _mentionsParsed = false;
        _attachmentsParsed = false;
        _embedParsed = false;
    }
    
    // Alias for CreateAsync
    public Task<TaskResult<Message>> PostAsync() => 
        CreateAsync();

    public override Message AddToCacheOrReturnExisting()
    {
        return Client.Cache.Messages.Put(Id, this);
    }

    public override Message TakeAndRemoveFromCache()
    {
        return Client.Cache.Messages.TakeAndRemove(Id);
    }
}
