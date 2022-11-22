namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class TextColor : StyleBase
{
    public Color Color { get; set; }

    public TextColor(Color color)
    {
        Color = color;
    }

    public override string ToString()
    {
        return $"color: {Color};";
    }
}
