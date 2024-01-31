using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Messages.Embeds.Styles.Basic;

public enum DecoractionType
{
	OverLine,
	LineThrough,
	UnderLine
}

public enum DecoractionStyle
{
	Solid,
	Double,
	Dotted,
	Dashed,
	Wavy
}

public class TextDecoration : StyleBase
{
	public static readonly TextDecoration UnderLine = new(DecoractionType.UnderLine);
	public static readonly TextDecoration LineThrough = new(DecoractionType.LineThrough);
	public static readonly TextDecoration OverLine = new(DecoractionType.OverLine);
	private readonly string[] _strings = new string[]
	{
		"text-decoration: overline",
		"text-decoration: linethrough",
		"text-decoration: underline"
	};

	private readonly string[] _styleStrings = new string[]
	{
		"solid",
		"double",
		"dotted",
		"dashed",
		"wavy"
	};

	[JsonPropertyName("t")]
	public DecoractionType Type { get; set; }

	[JsonPropertyName("s")]
	public DecoractionStyle? Style { get; set; }

	[JsonPropertyName("c")]
	public Color Color { get; set; }

	[JsonPropertyName("th")]
	public Size Thickness { get; set; }

	[JsonConstructor]
	public TextDecoration(DecoractionType type)
    {
        Type = type;
    }

	public TextDecoration(DecoractionType type, DecoractionStyle style, Color color, Size thickness)
	{
		Type = type;
		Style = style;
		Color = color;
		Thickness = thickness;
	}

	public override string ToString()
    {
		// Protect from updates or malformed data
		// causing exceptions by just ignoring
		// unknown styles
		if ((int)Type >= _strings.Length)
			return string.Empty;

		if (Style is null)
			return $"{_strings[(int)Type]};";
		else if (Color is null)
			return $"{_strings[(int)Type]} {_styleStrings[(int)Style]};";
		else if (Thickness is null)
			return $"{_strings[(int)Type]} {_styleStrings[(int)Style]} {Color};";
		else
			return $"{_strings[(int)Type]} {_styleStrings[(int)Style]} {Color} {Thickness};";
	}
}
