namespace Valour.Sdk.Models.Embeds.Styles;

/// <summary>
/// A single CSS declaration, e.g. "width: 50%;". Styles are compiled to
/// plain CSS strings at build time rather than serialized as objects.
/// </summary>
public readonly struct StyleValue
{
    public string Property { get; }
    public string Value { get; }

    public StyleValue(string property, string value)
    {
        Property = property;
        Value = value;
    }

    public override string ToString() => $"{Property}: {Value};";

    /// <summary>
    /// Joins declarations into a CSS string suitable for an item's Style.
    /// </summary>
    public static string Compile(params StyleValue[] styles) =>
        string.Join(" ", styles.Select(x => x.ToString()));
}
