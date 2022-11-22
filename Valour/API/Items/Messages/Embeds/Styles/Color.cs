namespace Valour.Api.Items.Messages.Embeds.Styles;

public struct Color
{
    public byte Red { get; set; }
    public byte Green { get; set; }
    public byte Blue { get; set; }
    public float Alpha { get; set; }

    public Color(byte red, byte green, byte blue, float alpha = 1f)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public override string ToString()
    {
        return $"rgba({Red}, {Green}, {Blue}, {Alpha})";
    }
}
