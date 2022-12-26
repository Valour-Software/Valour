using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class Padding : StyleBase
{
    [JsonPropertyName("l")]
    public Size Left { get; set; }

    [JsonPropertyName("r")]
    public Size Right { get; set; }

    [JsonPropertyName("t")]
    public Size Top { get; set; }

    [JsonPropertyName("b")]
    public Size Bottom { get; set; }

	[JsonPropertyName("o")]
	public Size Only { get; set; }

    internal Padding() { }

	public Padding(Size size)
    {
        Only = size;
    }

    public Padding(Size left, Size right, Size top, Size bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public override string ToString()
    {
        if (Only is null)
            return @$"padding-left: {Left};
                  padding-right: {Right};
                  padding-top: {Top};
                  padding-bottom: {Bottom};";
        else
			return @$"padding: {Only};";
	}
}
