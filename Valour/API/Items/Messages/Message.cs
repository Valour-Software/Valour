using System.Text.Json.Nodes;
using System.Text.Json;
using Valour.Api.Items.Messages.Attachments;
using Valour.Api.Items.Messages.Embeds;
using Valour.Shared.Items.Messages;
using Valour.Shared.Items.Messages.Mentions;
using Valour.Api.Items.Users;
using Valour.Shared;
using Valour.Api.Client;

namespace Valour.Api.Items.Messages;
public abstract class Message : Item, ISharedMessage
{
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
    /// True if the message was edited
    /// </summary>
    public bool Edited { get; set; }

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
    private bool mentionsParsed = false;

    /// <summary>
    /// The inner embed data
    /// </summary>
    private Embed _embed;

    /// <summary>
    /// True if the embed data has been parsed
    /// </summary>
    public bool embedParsed = false;

    /// <summary>
    /// The inner attachments data
    /// </summary>
    private List<MessageAttachment> _attachments;

    // Abstract methods
    public abstract ValueTask<Message> GetReplyMessageAsync();
    public abstract ValueTask<string> GetAuthorNameAsync();
    public abstract ValueTask<string> GetAuthorTagAsync();
    public abstract ValueTask<string> GetAuthorColorAsync();
    public abstract ValueTask<string> GetAuthorImageUrlAsync();

    public abstract Task<TaskResult> PostMessageAsync();
    public abstract Task<TaskResult> DeleteAsync();

    public virtual Task<bool> CheckIfMentioned() =>
        Task.FromResult(MentionsData?.Contains(ValourClient.Self.Id.ToString()) ?? false);

    /// <summary>
    /// The mentions for members within this message
    /// </summary>
    public List<Mention> Mentions
    {
        get
        {
            if (!mentionsParsed)
            {
                if (!string.IsNullOrEmpty(MentionsData))
                {
                    _mentions = JsonSerializer.Deserialize<List<Mention>>(MentionsData);
                }
            }

            return _mentions;
        }
    }

    public Embed Embed
    {
        get
        {
            if (!embedParsed)
            {
                if (!string.IsNullOrEmpty(EmbedData))
                {
                    Console.WriteLine(EmbedData);
                    _embed = JsonSerializer.Deserialize<Embed>(EmbedData);
                    foreach (var page in _embed.Pages)
                    {
                        if (page.Rows is not null)
                        { 
                            foreach (var row in page.Rows)
                            {
                                foreach(var item in row.Items)
                                {
                                    item.Embed = _embed;
                                }
                            }
                        }
						if (page.Items is not null)
						{
							foreach (var item in page.Items)
							{
								item.Embed = _embed;
							}
						}
					}
                }

                embedParsed = true;
            }

            return _embed;
        }
    }

    public List<MessageAttachment> Attachments
    {
        get
        {
            if (!mentionsParsed)
            {
                if (!string.IsNullOrEmpty(AttachmentsData))
                {
                    _attachments = JsonSerializer.Deserialize<List<MessageAttachment>>(AttachmentsData);
                }
            }

            return _attachments;
        }
    }

    public void SetMentions(IEnumerable<Mention> mentions)
    {
        _mentions = mentions.ToList();
        MentionsData = JsonSerializer.Serialize(mentions);
    }

    public void SetAttachments(List<MessageAttachment> attachments)
    {
        _attachments = attachments;
        AttachmentsData = JsonSerializer.Serialize(attachments);
    }

    public void ClearMentions()
    {
        if (_mentions == null)
        {
            _mentions = new List<Mention>();
        }
        else
        {
            MentionsData = null;
            _mentions.Clear();
        }
    }

    /// <summary>
    /// Returns true if the message is a embed
    /// </summary>
    public bool IsEmbed()
    {
        if (EmbedData != null || EmbedData == "")
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary> 
    /// Returns the author user of the message 
    /// </summary> 
    public async ValueTask<User> GetAuthorUserAsync(bool refresh = false) =>
        await User.FindAsync(AuthorUserId, refresh);
}
