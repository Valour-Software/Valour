using System.Text.Json.Serialization;

namespace Valour.Client.Components.Terminal;

/// <summary>
/// A run of terminal text. The class names are short style keys that the
/// terminal script maps to its palette, such as "green", "dim", or "bold".
/// </summary>
public sealed class TermSeg
{
    [JsonPropertyName("t")]
    public string Text { get; set; }

    [JsonPropertyName("c")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Style { get; set; }

    /// <summary>A hex color, used for role colors on names.</summary>
    [JsonPropertyName("s")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Color { get; set; }

    /// <summary>An http or https link opened when the run is clicked.</summary>
    [JsonPropertyName("h")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Href { get; set; }
}

/// <summary>
/// One line of terminal output. Plain lines are a list of runs. Chat lines
/// are laid out in three columns (time, name, body) so wrapped text stays
/// aligned under the body.
/// </summary>
public sealed class TermLine
{
    [JsonPropertyName("k")]
    public string Kind { get; set; } = "text";

    /// <summary>Lines with an id can be replaced later, for example on edit.</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Id { get; set; }

    [JsonPropertyName("segs")]
    public List<TermSeg> Segs { get; set; } = new();

    [JsonPropertyName("time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Time { get; set; }

    [JsonPropertyName("nick")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TermSeg> Nick { get; set; }

    /// <summary>Extra style keys for the whole line, such as "pending" or "mention".</summary>
    [JsonPropertyName("lc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string LineStyle { get; set; }

    public TermLine Add(string text, string style = null)
    {
        if (!string.IsNullOrEmpty(text))
            Segs.Add(new TermSeg { Text = text, Style = style });
        return this;
    }

    public TermLine AddColor(string text, string color, string style = null)
    {
        if (!string.IsNullOrEmpty(text))
            Segs.Add(new TermSeg { Text = text, Color = color, Style = style });
        return this;
    }

    public TermLine AddLink(string text, string href)
    {
        Segs.Add(new TermSeg { Text = text, Href = href, Style = "link" });
        return this;
    }

    public TermLine AddRange(IEnumerable<TermSeg> segs)
    {
        Segs.AddRange(segs);
        return this;
    }

    public static TermLine Of(string text, string style = null) => new TermLine().Add(text, style);

    public static TermLine Empty => new();
}
