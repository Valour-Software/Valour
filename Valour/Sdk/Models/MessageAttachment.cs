using Valour.Sdk.Client;
using Valour.Sdk.Nodes;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class MessageAttachment : ISharedMessageAttachment
{
    /// <summary>
    /// True if this attachment is local to the client, meaning it has not been uploaded to the server yet
    /// </summary>
    [JsonIgnore]
    public bool Local { get; set; } = false;
    
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }

    // Image attributes
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;

    public MessageAttachmentType Type { get; set; }
    
    /// <summary>
    /// True if this was an inline attachment - aka, it was generated using urls within the message
    /// </summary>
    public bool Inline { get; set; } = false;
    
    public string Html { get; set; }

    public MessageAttachment(MessageAttachmentType type)
    {
        Type = type;
    }
    
    private string _signedUrl;

    public async ValueTask<string?> GetSignedUrl(ValourClient client, Node node)
    {
        if (Local) // If the attachment is local, we don't need to fetch a signed URL
        {
            return Location;
        }
        
        var location = Location;
        
        // TODO: Make the base url not hard-coded
        if (!client.BaseAddress.StartsWith("https://app.valour.gg"))
        {
            // In dev environments, swap cdn for local server
            location = location.Replace("https://cdn.valour.gg/", "");
        }
        
        if (_signedUrl is null)
        {
            // Fetch url from CDN
            try
            {
                var result =
                    await node.GetAsync(location + "/signed");
                
                _signedUrl = result.Data;
            } 
            catch (Exception ex)
            {
                Console.WriteLine("Error fetching signed URL for attachment: " + ex.Message);
            }
        }
        
        return _signedUrl;
    }
}

