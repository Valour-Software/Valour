using Microsoft.AspNetCore.Components;
using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Client.Components.Messages.Attachments;

public class AttachmentComponent : ComponentBase
{
    [Parameter]
    public MessageAttachment Attachment { get; set; }

    [Parameter]
    public MessageComponent MessageComponent { get; set; }

    private string _signedUrl;

    public async ValueTask<string?> GetSignedUrl()
    {
        if (Attachment.Local) // If the attachment is local, we don't need to fetch a signed URL
        {
            return Attachment.Location;
        }
        
        var location = Attachment.Location;
        
        // TODO: Make the base url not hard-coded
        if (!MessageComponent.ParamData.Message.Client.BaseAddress.StartsWith("https://app.valour.gg"))
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
                    await MessageComponent.ParamData.Message.Node.GetAsync(location + "/signed");
                
                _signedUrl = result.Data;
            } 
            catch (Exception ex)
            {
                Console.WriteLine("Error fetching signed URL: " + ex.Message);
            }
        }
        
        return _signedUrl;
    }
}