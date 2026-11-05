using System.Text.RegularExpressions;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Messages;

/// <summary>
/// A piece of message text as the message input shows it. Text segments are
/// typed text. Mention segments become chips that render the same mention
/// components as chat messages. Emoji segments are custom planet emoji images.
/// </summary>
/// <param name="Kind">One of the kind constants in <see cref="MessageInputText"/>.</param>
/// <param name="Text">The typed text, or the token a chip or image stands for.</param>
/// <param name="Label">What to show before the chip's component renders, or the emoji's name.</param>
/// <param name="Src">The image address of a custom emoji.</param>
public sealed record MessageInputSegment(string Kind, string Text, string Label = null, string Src = null);

/// <summary>
/// Splits message text into the pieces the message input draws, so text that
/// is loaded into the input looks the same as text typed into it.
/// </summary>
public static class MessageInputText
{
    public const string TextKind = "text";
    public const string MentionKind = "mention";
    public const string EmojiKind = "emoji";

    private static readonly Regex TokenRegex = new(
        "«@(?<type>[mucr])-(?<id>[0-9]{1,20})»|«e-:(?<emoji>[a-z0-9_]{2,32}):~(?<emojiId>[0-9]{1,20})»",
        RegexOptions.Compiled);

    /// <summary>
    /// Reads a mention token such as «@m-123». The whole string must be one token.
    /// </summary>
    public static bool TryParseMention(string token, out Mention mention)
    {
        mention = null;
        if (string.IsNullOrEmpty(token))
            return false;

        var match = TokenRegex.Match(token);
        if (!match.Success || match.Index != 0 || match.Length != token.Length || !match.Groups["type"].Success)
            return false;

        if (!long.TryParse(match.Groups["id"].Value, out var id))
            return false;

        mention = new Mention
        {
            Type = Mention.CharToMentionType[match.Groups["type"].Value[0]],
            TargetId = id,
        };
        return true;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into segments. Mentions are labelled from
    /// cached models when they are loaded. Custom emoji need the planet to build
    /// their image address, so they stay as text when there is no planet.
    /// </summary>
    public static List<MessageInputSegment> Split(string text, Planet planet, ValourClient client)
    {
        var segments = new List<MessageInputSegment>();
        if (string.IsNullOrEmpty(text))
            return segments;

        var position = 0;
        foreach (Match match in TokenRegex.Matches(text))
        {
            MessageInputSegment segment = null;
            if (match.Groups["type"].Success)
            {
                if (TryParseMention(match.Value, out var mention))
                    segment = new MessageInputSegment(MentionKind, match.Value, GetLabel(mention, planet, client));
            }
            else if (planet is not null && long.TryParse(match.Groups["emojiId"].Value, out var emojiId))
            {
                segment = new MessageInputSegment(
                    EmojiKind,
                    match.Value,
                    $":{match.Groups["emoji"].Value}:",
                    ISharedPlanetEmoji.GetCdnUrl(planet.Id, emojiId));
            }

            if (segment is null)
                continue;

            if (match.Index > position)
                segments.Add(new MessageInputSegment(TextKind, text[position..match.Index]));

            segments.Add(segment);
            position = match.Index + match.Length;
        }

        if (position < text.Length)
            segments.Add(new MessageInputSegment(TextKind, text[position..]));

        return segments;
    }

    private static string GetLabel(Mention mention, Planet planet, ValourClient client) => mention.Type switch
    {
        MentionType.PlanetMember => "@" + planet?.Members.Get(mention.TargetId)?.Name,
        MentionType.User => "@" + client?.Cache.Users.Get(mention.TargetId)?.Name,
        MentionType.Role => "@" + planet?.Roles.Get(mention.TargetId)?.Name,
        MentionType.Channel => "#" + planet?.Channels.Get(mention.TargetId)?.Name,
        _ => string.Empty,
    };
}
