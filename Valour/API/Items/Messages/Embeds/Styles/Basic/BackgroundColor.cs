namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class BackgroundColor : IStyle
{
    public Color Color { get; set; }

    public BackgroundColor(Color color)
    {
        Color = color;
        Type = EmbedStyleType.BackgroundColor;
    }

    public override string ToString()
    {
        return $"background-color: {Color};";
    }
}
