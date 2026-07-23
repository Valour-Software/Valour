using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds.Items;

public enum EmbedClickTargetType
{
    Link = 1,
    Page = 2,
    Event = 3,
    SubmitForm = 4,
}

/// <summary>
/// What happens when a clickable embed item is clicked.
/// </summary>
[JsonDerivedType(typeof(EmbedLinkTarget), "link")]
[JsonDerivedType(typeof(EmbedPageTarget), "page")]
[JsonDerivedType(typeof(EmbedEventTarget), "event")]
[JsonDerivedType(typeof(EmbedFormSubmitTarget), "submit")]
public abstract class EmbedClickTarget
{
    [JsonIgnore]
    public abstract EmbedClickTargetType Type { get; }
}

/// <summary>
/// Opens an external link (after user confirmation).
/// </summary>
public class EmbedLinkTarget : EmbedClickTarget
{
    public string? Href { get; set; }

    [JsonIgnore]
    public override EmbedClickTargetType Type => EmbedClickTargetType.Link;
}

/// <summary>
/// Navigates the embed to another page.
/// </summary>
public class EmbedPageTarget : EmbedClickTarget
{
    public int PageIndex { get; set; }

    [JsonIgnore]
    public override EmbedClickTargetType Type => EmbedClickTargetType.Page;
}

/// <summary>
/// Sends an interaction event to the bot that authored the embed.
/// </summary>
public class EmbedEventTarget : EmbedClickTarget
{
    public string? EventId { get; set; }

    [JsonIgnore]
    public override EmbedClickTargetType Type => EmbedClickTargetType.Event;
}

/// <summary>
/// Submits the enclosing form, sending its input values to the bot.
/// </summary>
public class EmbedFormSubmitTarget : EmbedClickTarget
{
    public string? EventId { get; set; }

    [JsonIgnore]
    public override EmbedClickTargetType Type => EmbedClickTargetType.SubmitForm;
}
