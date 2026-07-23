using Valour.Sdk.Models.Embeds.Items;

namespace Valour.Sdk.Models.Embeds;

public enum EmbedInteractionEventType
{
    ItemClicked = 1,
    FormSubmitted = 2,
}

/// <summary>
/// What a client sends when a user interacts with an embed. The server
/// derives all context (channel, planet, members) from the message itself,
/// so clients only report what was interacted with.
/// </summary>
public class EmbedInteractionRequest
{
    public long MessageId { get; set; }

    public EmbedInteractionEventType EventType { get; set; }

    /// <summary>
    /// The event id of the clicked element, from its click target.
    /// </summary>
    public string? ElementId { get; set; }

    /// <summary>
    /// The id of the submitted form, for form submissions.
    /// </summary>
    public string? FormId { get; set; }

    public List<EmbedFormData>? FormData { get; set; }
}

/// <summary>
/// The interaction event relayed to bots. All contextual fields are
/// stamped by the server; only the element/form data originates
/// from the interacting client.
/// </summary>
public class EmbedInteractionEvent
{
    public EmbedInteractionEventType EventType { get; set; }

    public string? ElementId { get; set; }

    public string? FormId { get; set; }

    public List<EmbedFormData>? FormData { get; set; }

    public long MessageId { get; set; }

    public long ChannelId { get; set; }

    public long PlanetId { get; set; }

    /// <summary>
    /// The planet member of the message author (the bot).
    /// </summary>
    public long AuthorMemberId { get; set; }

    /// <summary>
    /// The planet member who interacted.
    /// </summary>
    public long MemberId { get; set; }

    public DateTime TimeInteracted { get; set; }
}
