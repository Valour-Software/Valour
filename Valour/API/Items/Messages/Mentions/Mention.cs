using System.Text.Json.Serialization;
using Valour.Shared.Items.Messages.Mentions;

namespace Valour.Api.Items.Messages.Mentions;

public class Mention : ISharedMention
{
    /// <summary>
    /// The type of mention this is
    /// </summary>
    [JsonPropertyName("Type")]
    public MentionType Type { get; set; }

    /// <summary>
    /// The item id being mentioned
    /// </summary>
    [JsonPropertyName("Target_Id")]
    public ulong Target_Id { get; set; }

    /// <summary>
    /// The position of the mention, in chars.
    /// For example, the message "Hey @SpikeViper!" would have Position = 4
    /// </summary>
    [JsonPropertyName("Position")]
    public ushort Position { get; set; }

    /// <summary>
    /// The length of this mention, in chars
    /// </summary>
    [JsonPropertyName("Length")]
    public ushort Length { get; set; }
}

