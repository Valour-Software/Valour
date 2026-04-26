using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class MessageAttachment : ISharedMessageAttachment
{
    public const string MissingLocation = "valour://attachment-not-found";
    public const string EmbedLocation = "valour://embed";

    /// <summary>
    /// True if this attachment is local to the client, meaning it has not been uploaded to the server yet
    /// </summary>
    [JsonIgnore]
    public bool Local { get; set; } = false;
    
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }
    public long Id { get; set; }
    public long MessageId { get; set; }
    public int SortOrder { get; set; }
    public string CdnBucketItemId { get; set; }

    // Image attributes
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;

    public MessageAttachmentType Type { get; set; }
    
    /// <summary>
    /// True if this was an inline attachment - aka, it was generated using urls within the message
    /// </summary>
    public bool Inline { get; set; } = false;

    /// <summary>
    /// True when the original upload was deleted or quarantined and this row is a tombstone.
    /// </summary>
    public bool Missing { get; set; } = false;

    /// <summary>
    /// Type-specific payload for attachment types that need one, such as oEmbed HTML or Valour embed JSON.
    /// </summary>
    public string Data { get; set; }

    /// <summary>
    /// Open Graph data for site preview attachments
    /// </summary>
    public OpenGraphData OpenGraph { get; set; }

    public MessageAttachment()
    {
    }

    public MessageAttachment(MessageAttachmentType type)
    {
        Type = type;
    }

    public static MessageAttachment CreateMissing(string fileName = null)
    {
        return new MessageAttachment(MessageAttachmentType.File)
        {
            Location = MissingLocation,
            MimeType = "application/octet-stream",
            FileName = string.IsNullOrWhiteSpace(fileName) ? "Attachment not found" : fileName,
            Missing = true
        };
    }

    public static MessageAttachment CreateEmbed(Embed embed)
    {
        var attachment = new MessageAttachment(MessageAttachmentType.Embed)
        {
            Location = EmbedLocation,
            MimeType = "application/vnd.valour.embed+json",
            FileName = "Embed"
        };

        attachment.SetEmbed(embed);
        return attachment;
    }

    private Embed _embed;
    private bool _embedParsed;

    [JsonIgnore]
    public Embed Embed
    {
        get
        {
            if (Type != MessageAttachmentType.Embed)
                return null;

            if (!_embedParsed)
            {
                _embed = ParseEmbed(Data);
                _embedParsed = true;
            }

            return _embed;
        }
    }

    public void SetEmbed(Embed embed)
    {
        Type = MessageAttachmentType.Embed;
        Location = EmbedLocation;
        MimeType = "application/vnd.valour.embed+json";
        FileName ??= "Embed";
        Data = embed is null ? null : JsonSerializer.Serialize(embed);
        _embed = embed;
        _embedParsed = true;

        if (_embed is not null)
            InitEmbed(_embed);
    }

    public void SetEmbedPayload(string data)
    {
        Type = MessageAttachmentType.Embed;
        Location = EmbedLocation;
        MimeType = "application/vnd.valour.embed+json";
        FileName ??= "Embed";
        Data = data;
        _embed = null;
        _embedParsed = false;
    }

    public void SetEmbedParsed(bool val)
    {
        _embedParsed = val;
        if (!val)
            _embed = null;
    }

    private static Embed ParseEmbed(string data)
    {
        if (string.IsNullOrEmpty(data))
            return null;

        // prevent a million errors in console for legacy embed versions
        if (data.Contains("EmbedVersion\":\"1.1.0\""))
            return null;

        var embed = JsonSerializer.Deserialize<Embed>(data);
        InitEmbed(embed);
        return embed;
    }

    private static void InitEmbed(Embed embed)
    {
        if (embed?.Pages is null)
            return;

        foreach (var page in embed.Pages)
        {
            if (page.Children is null)
                continue;

            foreach (var item in page.Children)
            {
                item.Embed = embed;
                item.Init(embed, page);
            }
        }
    }

    private string _signedUrl;

    public async ValueTask<string?> GetSignedUrl(ValourClient client, Node node)
    {
        if (Local && IsBrowserLocalLocation(Location)) // Browser-local previews do not need a signed URL.
        {
            return Location;
        }

        if (Missing || string.IsNullOrWhiteSpace(Location) || Location == MissingLocation)
        {
            return null;
        }
        
        var location = Location;

        if (location.StartsWith("https://media.tenor.com"))
        {
            // If the location is a Tenor URL, we don't need to fetch a signed URL
            return location;
        }
        
        var uri = new Uri(location);
        
        // Strip the protocol and host from the location
        // This is because the CDN will return a signed URL that is relative to the base URL of the client
        location = uri.PathAndQuery.TrimStart('/');
        
        if (location.Contains("proxy/"))
        {
            // If the location is a proxy URL, we don't need to fetch a signed URL
            return location;
        }
        
        if (_signedUrl is null)
        {
            // Fetch url from CDN
            try
            {
                var url = location + "/signed";
                var result = await node.GetAsync(url);
                
                _signedUrl = result.Data;
            } 
            catch (Exception ex)
            {
                Console.WriteLine("Error fetching signed URL for attachment: " + ex.Message);
            }
        }

        return _signedUrl;
    }

    private static bool IsBrowserLocalLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return false;

        return location.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
               location.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }
}

