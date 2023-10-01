using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Api.Models.Messages.Embeds;
using Valour.Shared.Models;
using Valour.Shared.Models;
using Valour.Shared;
using Valour.Api.Client;
using Valour.Api.Models;

namespace Valour.Api.Models;

[JsonDerivedType(typeof(PlanetMessage), typeDiscriminator: nameof(PlanetMessage))]
[JsonDerivedType(typeof(DirectMessage), typeDiscriminator: nameof(DirectMessage))]
public abstract class Message : LiveModel, ISharedMessage
{
    public Message()
    {
        
    }

    public abstract Message GetReply();

    /// <summary>
    /// The message (if any) this is a reply to
    /// </summary>
    public long? ReplyToId { get; set; }

    /// <summary>
    /// The author's user ID
    /// </summary>
    public long AuthorUserId { get; set; }

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
    /// Index of the message
    /// </summary>
    [Obsolete("Message Id should be used instead.", true)]
    public long MessageIndex { get; set; }

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

    // Abstract methods
    public abstract ValueTask<Message> GetReplyMessageAsync();
    public abstract ValueTask<string> GetAuthorNameAsync();
    public abstract ValueTask<string> GetAuthorTagAsync();
    public abstract ValueTask<string> GetAuthorColorAsync();
    public abstract ValueTask<string> GetAuthorImageUrlAsync();

    public abstract Task<TaskResult> PostMessageAsync();
    public abstract Task<TaskResult> EditMessageAsync();
    public abstract Task<TaskResult> DeleteAsync();

    public virtual Task<bool> CheckIfMentioned()
    {
        if (Mentions is null || Mentions.Count == 0)
            return Task.FromResult(false);
        
        return Task.FromResult(Mentions.Any(x => x.TargetId == ValourClient.Self.Id));
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
                    foreach (var page in _embed.Pages)
                    {
                        foreach (var item in page.Children)
                        {
                            item.Embed = _embed;
                            item.Init(_embed, page);
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
}
