using System.Text.Json;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Shared.Models;
using Valour.Shared;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using Valour.Sdk.Utility;

namespace Valour.Sdk.Models;

public class Message : ClientPlanetModel<Message, long>, ISharedMessage
{
    public override string BaseRoute =>
        $"api/channels/{ChannelId}/messages";
    
    #region Database fields
    
    /// <summary>
    /// The planet (if any) this message belongs to
    /// </summary>
    public long? PlanetId { get; set; }
    public override long? GetPlanetId() => PlanetId;

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
    
    #endregion

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
    public async ValueTask<Message> GetReplyMessageAsync()
    {
        if (ReplyTo is not null)
            return ReplyTo;

        if (ReplyToId is null)
            return null;
        
        return await FindAsync(ReplyToId.Value, ChannelId);
    }

    public ValueTask<Channel> GetChannelAsync()
    {
        return Channel.FindAsync(ChannelId);
    }

    /// <summary>
    /// Returns the user for the author of this message
    /// </summary>
    public async ValueTask<User> GetAuthorUserAsync()
    {
        _authorUserCached ??= await User.FindAsync(AuthorUserId);
        return _authorUserCached;
    }
    
    /// <summary>
    /// Returns the planet member for the author of this message (if any)
    /// </summary>
    public async ValueTask<PlanetMember> GetAuthorMemberAsync()
    {
        if (_authorMemberCached is not null)
            return _authorMemberCached;

        if (AuthorMemberId is null)
            return null;
        
        _authorMemberCached = await Planet.FetchMemberAsync(AuthorMemberId.Value);
        
        return _authorMemberCached;
    }

    /// <summary>
    /// Returns the name that should be displayed for the author of this message
    /// </summary>
    public async ValueTask<string> GetAuthorNameAsync()
    {
        // Check for member first
        var member = await GetAuthorMemberAsync();
        if (member is not null)
        {
            // They may not have a nickname!
            if (member.Nickname is not null)
            {
                return member.Nickname;
            }
        }
        
        // If we get here, the member name was not found
        // so we fall back on the user (which should always exist)
        var user = await GetAuthorUserAsync();
        return user?.Name ?? "User not found";
    }

    public async ValueTask<string> GetAuthorRoleTagAsync()
    {
        // If there's a member, we use the member's planet role
        var author = await GetAuthorMemberAsync();
        if (author is not null)
        {
            return (await author.GetPrimaryRoleAsync()).Name ?? "Unknown Role";
        }
        
        // Otherwise, we use their relationship with the user
        var user = await GetAuthorUserAsync();
        if (user.Id == ValourClient.Self.Id)
            return "You";

        if (user.Bot)
            return "Bot";

        if (ValourClient.FriendFastLookup.Contains(user.Id))
            return "Friend";
        
        return "User";
    }

    public async ValueTask<string> GetAuthorColorAsync()
    {
        var member = await GetAuthorMemberAsync();
        if (member is not null)
        {
            return await member.GetRoleColorAsync();
        }
        
        if (ValourClient.FriendFastLookup.Contains(AuthorUserId))
            return "#9ffff1";

        return "#ffffff";
    }

    public async ValueTask<string> GetAuthorImageUrlAsync()
    {
        var member = await GetAuthorMemberAsync();
        var user = await GetAuthorUserAsync();
        return AvatarUtility.GetAvatarUrl(user, member, AvatarFormat.Webp128);
    }
    
    public async ValueTask<string> GetAuthorImageUrlAnimatedAsync()
    {   
        // TODO: Tweak when member avatars are implemented
        var user = await GetAuthorUserAsync();
        if (!user.HasAnimatedAvatar)
            return null;
        
        var member = await GetAuthorMemberAsync();
        return AvatarUtility.GetAvatarUrl(user, member, AvatarFormat.WebpAnimated128);
    }
    
    public async ValueTask<string> GetAuthorImageUrlFallbackAsync()
    {
        var user = await GetAuthorUserAsync();
        return user.GetFailedAvatarUrl();
    }
    
    public async Task<bool> CheckIfMentioned()
    {
        if (Mentions is null || Mentions.Count == 0)
            return false;

        List<PlanetRole> selfRoles = null;
        PlanetMember selfMember = null;

        foreach (var mention in Mentions)
        {
            switch (mention.Type)
            {
                case MentionType.User:
                {
                    if (mention.TargetId == ValourClient.Self.Id)
                    {
                        return true;
                    }

                    break;
                }
                case MentionType.PlanetMember:
                {
                    if (PlanetId is null)
                        continue;
                    
                    if (selfMember is null)
                    {
                        selfMember = await Planet.FetchSelfMemberAsync();
                    }
                    
                    if (mention.TargetId == selfMember.Id)
                        return true;
                    
                    break;
                }
                case MentionType.Role:
                {
                    if (PlanetId is null)
                        continue;
                    
                    if (selfRoles is null)
                    {
                        if (selfMember is null)
                        {
                            selfMember = await Planet.FetchSelfMemberAsync();
                        }
                        
                        selfRoles = await selfMember.GetRolesAsync();
                    }
                    
                    foreach (var role in selfRoles)
                    {
                        if (mention.TargetId == role.Id)
                            return true;
                    }

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

    /// <summary> 
    /// Returns the author user of the message 
    /// </summary> 
    public async ValueTask<User> GetAuthorUserAsync(bool refresh = false) =>
        await User.FindAsync(AuthorUserId, refresh);

    public bool IsEmpty()
    {
        return string.IsNullOrWhiteSpace(Content) && 
               string.IsNullOrWhiteSpace(EmbedData) &&
               string.IsNullOrWhiteSpace(AttachmentsData) && 
               (_attachments is null || _attachments.Count == 0) && 
               ReplyToId is null;
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
    
    
    #region Route implementations
    
    public static async ValueTask<Message> FindAsync(long id, long channelId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ModelCache<,>.Get<Message>(id);
            if (cached is not null)
                return cached;
        }
        var response = await ValourClient.PrimaryNode.GetJsonAsync<Message>($"api/channels/{channelId}/message/{id}");
        var item = response.Data;

        if (item is not null)
        {
            await item.AddToCache(item);
        }

        return item;
    }
    
    public Task<TaskResult> DeleteAsync() =>
        Node.DeleteAsync($"api/channels/{ChannelId}/messages/{Id}");
    
    public async Task<TaskResult> PostMessageAsync()
    {
        var node = ValourClient.PrimaryNode;
        if (PlanetId is not null)
        {
            node = await NodeManager.GetNodeForPlanetAsync(PlanetId.Value);
        }
        
        return await node.PostAsync($"api/channels/{ChannelId}/messages", this);
    }
    
    public async Task<TaskResult> EditMessageAsync()
    {
        var node = ValourClient.PrimaryNode;
        if (PlanetId is not null)
        {
            node = await NodeManager.GetNodeForPlanetAsync(PlanetId.Value);
        }
        
        return await node.PutAsync($"api/channels/{ChannelId}/messages/{Id}", this);
    }
    
    #endregion
}
