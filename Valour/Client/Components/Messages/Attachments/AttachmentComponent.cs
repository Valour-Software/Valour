using Microsoft.AspNetCore.Components;
using Valour.Client.Components.Messages;
using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Client.Components.Messages.Attachments;

public class AttachmentComponent : ComponentBase
{
    [Parameter]
    public Message Message { get; set; }
    
    [Parameter]
    public MessageAttachment Attachment { get; set; }

    [Parameter]
    public MessageComponent MessageComponent { get; set; }

    /// <summary>
    /// Returns the attachment location if it is an https URL on one of the
    /// provider's embed hosts, otherwise null. Provider attachments render
    /// their location as an iframe src, so anything else must not render.
    /// </summary>
    protected string GetTrustedEmbedSource(params string[] hosts)
    {
        if (!Uri.TryCreate(Attachment?.Location, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return null;

        foreach (var host in hosts)
        {
            if (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
                return uri.AbsoluteUri;
        }

        return null;
    }
}
