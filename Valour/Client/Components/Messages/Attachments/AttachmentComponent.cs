using Microsoft.AspNetCore.Components;
using Valour.Sdk.Models;

namespace Valour.Client.Components.Messages.Attachments;

public class AttachmentComponent : ComponentBase
{
    [Parameter]
    public MessageAttachment Attachment { get; set; }

    [Parameter]
    public MessageComponent MessageComponent { get; set; }
}