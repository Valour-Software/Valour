namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public struct FontSize : IStyle
{
    public Size Size { get; set; }

    public FontSize(Size size)
    {
        Size = size;
    }

    public override string ToString()
    {
        return $"font-size: {Size};";
    }
}
