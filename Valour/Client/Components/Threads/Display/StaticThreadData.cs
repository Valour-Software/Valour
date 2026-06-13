namespace Valour.Client.Components.Threads.Display;

/// <summary>
/// Plain data for rendering a thread post without SDK models.
/// Used by the static public pages, where content is pre-rendered server-side.
/// </summary>
public class StaticThreadPostData
{
    public string Title { get; set; }
    public string AuthorName { get; set; }
    public string AuthorAvatarUrl { get; set; }

    /// <summary>
    /// Primary role of the author, shown like the chat header (colored name + role tag)
    /// </summary>
    public string AuthorRoleName { get; set; }
    public string AuthorRoleColor { get; set; }

    public DateTime TimeCreated { get; set; }
    public bool Edited { get; set; }
    public bool IsPinned { get; set; }
    public bool IsLocked { get; set; }
    public bool Nsfw { get; set; }
    public int BoostCount { get; set; }
    public int CommentCount { get; set; }

    /// <summary>
    /// Pre-rendered, sanitized HTML for the post body
    /// </summary>
    public string ContentHtml { get; set; }

    public List<StaticAttachmentData> Attachments { get; set; }
}

public class StaticAttachmentData
{
    public string Url { get; set; }
    public string FileName { get; set; }
    public bool IsImage { get; set; }
    public bool IsVideo { get; set; }
}

/// <summary>
/// Plain data for rendering a comment tree without SDK models
/// </summary>
public class StaticCommentData
{
    public string AuthorName { get; set; }
    public string AuthorAvatarUrl { get; set; }

    /// <summary>
    /// Primary role of the author, shown like the chat header (colored name + role tag)
    /// </summary>
    public string AuthorRoleName { get; set; }
    public string AuthorRoleColor { get; set; }

    public DateTime TimeCreated { get; set; }
    public bool Edited { get; set; }
    public bool Deleted { get; set; }
    public int BoostCount { get; set; }

    /// <summary>
    /// Pre-rendered, sanitized HTML for the comment body
    /// </summary>
    public string ContentHtml { get; set; }

    public List<StaticCommentData> Replies { get; set; }
}
