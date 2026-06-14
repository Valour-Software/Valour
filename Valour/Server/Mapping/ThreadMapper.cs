using System.Text.Json;
using Valour.Server.Database;
using Valour.Shared.Models;

namespace Valour.Server.Mapping;

public static class ThreadMapper
{
    public static PlanetThread ToModel(this Valour.Database.PlanetThread thread)
    {
        if (thread is null)
            return null;

        return new PlanetThread()
        {
            Id = thread.Id,
            PlanetId = thread.PlanetId,
            AuthorUserId = thread.AuthorUserId,
            AuthorMemberId = thread.AuthorMemberId,
            Title = thread.Title,
            Content = thread.Content,
            TimeCreated = thread.TimeCreated,
            EditedTime = thread.EditedTime,
            IsLocked = thread.IsLocked,
            Nsfw = thread.Nsfw,
            BoostCount = thread.BoostCount,
            CommentCount = thread.CommentCount,
            Attachments = thread.Attachments?
                .OrderBy(x => x.SortOrder)
                .Select(x => x.ToModel())
                .ToList()
        };
    }

    public static Valour.Database.PlanetThread ToDatabase(this PlanetThread thread)
    {
        if (thread is null)
            return null;

        return new Valour.Database.PlanetThread()
        {
            Id = thread.Id,
            PlanetId = thread.PlanetId,
            AuthorUserId = thread.AuthorUserId,
            AuthorMemberId = thread.AuthorMemberId,
            Title = thread.Title,
            Content = thread.Content,
            TimeCreated = thread.TimeCreated,
            EditedTime = thread.EditedTime,
            IsLocked = thread.IsLocked,
            Nsfw = thread.Nsfw,
            BoostCount = thread.BoostCount,
            CommentCount = thread.CommentCount,
            Attachments = thread.Attachments?
                .Select((x, i) => x.ToThreadAttachment(thread.Id, i))
                .ToList()
        };
    }

    public static Valour.Sdk.Models.MessageAttachment ToModel(this Valour.Database.ThreadAttachment attachment)
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

    public static Valour.Database.ThreadAttachment ToThreadAttachment(
        this Valour.Sdk.Models.MessageAttachment attachment,
        long threadId,
        int sortOrder)
    {
        if (attachment is null)
            return null;

        return new Valour.Database.ThreadAttachment()
        {
            Id = attachment.Id == 0 ? IdManager.Generate() : attachment.Id,
            ThreadId = threadId,
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

    public static ThreadComment ToModel(this Valour.Database.ThreadComment comment)
    {
        if (comment is null)
            return null;

        return new ThreadComment()
        {
            Id = comment.Id,
            PlanetId = comment.PlanetId,
            ThreadId = comment.ThreadId,
            ParentCommentId = comment.ParentCommentId,
            Depth = comment.Depth,
            AuthorUserId = comment.AuthorUserId,
            AuthorMemberId = comment.AuthorMemberId,
            Content = comment.IsDeleted ? string.Empty : comment.Content,
            TimeCreated = comment.TimeCreated,
            EditedTime = comment.EditedTime,
            BoostCount = comment.BoostCount,
            ReplyCount = comment.ReplyCount,
            IsDeleted = comment.IsDeleted
        };
    }

    public static Valour.Database.ThreadComment ToDatabase(this ThreadComment comment)
    {
        if (comment is null)
            return null;

        return new Valour.Database.ThreadComment()
        {
            Id = comment.Id,
            PlanetId = comment.PlanetId,
            ThreadId = comment.ThreadId,
            ParentCommentId = comment.ParentCommentId,
            Depth = comment.Depth,
            AuthorUserId = comment.AuthorUserId,
            AuthorMemberId = comment.AuthorMemberId,
            Content = comment.Content,
            TimeCreated = comment.TimeCreated,
            EditedTime = comment.EditedTime,
            BoostCount = comment.BoostCount,
            ReplyCount = comment.ReplyCount,
            IsDeleted = comment.IsDeleted
        };
    }
}
