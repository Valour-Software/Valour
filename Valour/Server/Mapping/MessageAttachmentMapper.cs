using System.Text.Json;
using Valour.Server.Database;
using Valour.Shared.Models;

namespace Valour.Server.Mapping;

public static class MessageAttachmentMapper
{
    public static Valour.Sdk.Models.MessageAttachment ToModel(this Valour.Database.MessageAttachment attachment)
    {
        if (attachment is null)
            return null;

        OpenGraphData openGraph = null;
        if (!string.IsNullOrWhiteSpace(attachment.OpenGraphData))
        {
            try
            {
                openGraph = JsonSerializer.Deserialize<OpenGraphData>(attachment.OpenGraphData);
            }
            catch
            {
                openGraph = null;
            }
        }

        return new Valour.Sdk.Models.MessageAttachment()
        {
            Id = attachment.Id,
            MessageId = attachment.MessageId,
            SortOrder = attachment.SortOrder,
            Type = attachment.Type,
            CdnBucketItemId = attachment.CdnBucketItemId,
            Location = attachment.Location,
            MimeType = attachment.MimeType,
            FileName = attachment.FileName,
            Width = attachment.Width,
            Height = attachment.Height,
            Inline = attachment.Inline,
            Missing = attachment.Missing,
            Data = attachment.Data,
            OpenGraph = openGraph
        };
    }

    public static Valour.Database.MessageAttachment ToDatabase(
        this Valour.Sdk.Models.MessageAttachment attachment,
        long messageId,
        int sortOrder)
    {
        if (attachment is null)
            return null;

        return new Valour.Database.MessageAttachment()
        {
            Id = attachment.Id == 0 ? IdManager.Generate() : attachment.Id,
            MessageId = messageId,
            SortOrder = sortOrder,
            Type = attachment.Type,
            CdnBucketItemId = attachment.CdnBucketItemId,
            Location = attachment.Location,
            MimeType = attachment.MimeType,
            FileName = attachment.FileName,
            Width = attachment.Width,
            Height = attachment.Height,
            Inline = attachment.Inline,
            Missing = attachment.Missing,
            Data = attachment.Data,
            OpenGraphData = attachment.OpenGraph is null ? null : JsonSerializer.Serialize(attachment.OpenGraph)
        };
    }
}
