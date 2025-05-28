using Microsoft.AspNetCore.Components;
using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Client.Components.Messages.Attachments;

public class AttachmentComponent : ComponentBase
{
    [Parameter]
    public Message Message { get; set; }
    
    [Parameter]
    public MessageAttachment Attachment { get; set; }
}