using System.Text.Json;
using Valour.Sdk.Models.Embeds.Items;
using Valour.Shared;

namespace Valour.Sdk.Models.Embeds;

/// <summary>
/// Serialization, version guarding, and validation for embeds.
/// Shared by the client (parsing message attachments and live updates)
/// and the server (validating bot-submitted embeds).
/// </summary>
public static class EmbedParser
{
    public const int MaxPayloadLength = 65535;

    /// <summary>
    /// CSS properties bot-authored styles may use. Compiled style strings
    /// are rejected server-side if they use anything else.
    /// </summary>
    private static readonly HashSet<string> AllowedCssProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "width", "min-width", "max-width",
        "height", "min-height", "max-height",
        "background-color", "color", "opacity",
        "font-size", "font-weight", "font-style", "text-decoration", "text-align",
        "margin", "margin-left", "margin-right", "margin-top", "margin-bottom",
        "padding", "padding-left", "padding-right", "padding-top", "padding-bottom",
        "border", "border-radius",
        "display", "position", "top", "right", "bottom", "left", "overflow",
        "flex-direction", "flex-wrap", "justify-content", "align-items",
        "align-content", "align-self", "flex-grow", "flex-shrink", "order", "gap",
    };

    private static readonly string[] ForbiddenCssFragments =
    {
        "url(", "expression(", "@", "/*", "\\", "<", ">",
    };

    /// <summary>
    /// Parses an embed payload. Returns null for anything invalid: empty or
    /// oversized data, legacy (v1) payloads, malformed JSON, or a version
    /// mismatch. Never throws and never logs.
    /// </summary>
    public static Embed? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxPayloadLength)
            return null;

        // Legacy embeds (v1.x) serialized an "EmbedVersion" string property
        if (json.Contains("\"EmbedVersion\""))
            return null;

        Embed? embed;
        try
        {
            embed = JsonSerializer.Deserialize<Embed>(json);
        }
        catch (Exception)
        {
            return null;
        }

        if (embed is null || embed.Version != Embed.CurrentVersion)
            return null;

        if (embed.Pages is null || embed.Pages.Count == 0)
            return null;

        return embed;
    }

    /// <summary>
    /// Parses a targeted-update item list. Returns null if malformed.
    /// </summary>
    public static List<EmbedItem>? TryParseItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxPayloadLength)
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<EmbedItem>>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string Serialize(Embed embed) => JsonSerializer.Serialize(embed);

    /// <summary>
    /// Validates embed structure and bot-authored styles. Used by the server
    /// before accepting an embed, and by <see cref="EmbedBuilder.Build"/>.
    /// </summary>
    public static TaskResult Validate(Embed embed)
    {
        if (embed.Pages is null || embed.Pages.Count == 0)
            return TaskResult.FromFailure("Embed must have at least one page.");

        var seenIds = new HashSet<string>();

        foreach (var page in embed.Pages)
        {
            if (page.Children is null)
                return TaskResult.FromFailure("Embed page is missing its children list.");

            var styleResult = ValidateCss(page.TitleStyle)
                .Otherwise(() => ValidateCss(page.FooterStyle))
                .Otherwise(() => ValidateCss(page.Style));
            if (!styleResult.Success)
                return styleResult;
        }

        foreach (var item in embed.EnumerateItems())
        {
            if (item.Id is not null && !seenIds.Add(item.Id))
                return TaskResult.FromFailure($"Duplicate item id '{item.Id}'. Item ids must be unique across the embed.");

            var result = ValidateItemPresentation(item);
            if (!result.Success)
                return result;
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates the styles and classes of a set of items (targeted updates).
    /// </summary>
    public static TaskResult ValidateItems(IEnumerable<EmbedItem> items)
    {
        foreach (var root in items)
        {
            var result = ValidateItemPresentation(root);
            if (!result.Success)
                return result;

            foreach (var item in root.EnumerateDescendants())
            {
                result = ValidateItemPresentation(item);
                if (!result.Success)
                    return result;
            }
        }

        return TaskResult.SuccessResult;
    }

    private static TaskResult ValidateItemPresentation(EmbedItem item)
    {
        var result = ValidateCss(item.Style);
        if (!result.Success)
            return result;

        return ValidateClasses(item.Classes);
    }

    /// <summary>
    /// Validates a compiled CSS declaration string against the property
    /// whitelist and forbidden value fragments.
    /// </summary>
    public static TaskResult ValidateCss(string? style)
    {
        if (string.IsNullOrWhiteSpace(style))
            return TaskResult.SuccessResult;

        foreach (var fragment in ForbiddenCssFragments)
        {
            if (style.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return TaskResult.FromFailure($"Embed style contains forbidden content: '{fragment}'.");
        }

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0)
                return TaskResult.FromFailure($"Embed style declaration '{declaration}' is malformed.");

            var property = declaration[..colon].Trim();
            if (!AllowedCssProperties.Contains(property))
                return TaskResult.FromFailure($"Embed style property '{property}' is not allowed.");
        }

        return TaskResult.SuccessResult;
    }

    private static TaskResult ValidateClasses(string? classes)
    {
        if (string.IsNullOrWhiteSpace(classes))
            return TaskResult.SuccessResult;

        foreach (var token in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
                return TaskResult.FromFailure($"Embed class '{token}' contains invalid characters.");
        }

        return TaskResult.SuccessResult;
    }

    private static TaskResult Otherwise(this TaskResult result, Func<TaskResult> next) =>
        result.Success ? next() : result;
}
