namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public struct BorderRadius : IStyle
{
    public Size TopLeft { get; set; }
    public Size TopRight { get; set; }
    public Size BottomLeft { get; set; } 
    public Size BottomRight { get; set; }

    public BorderRadius(Size size)
    {
        TopLeft = size;
        TopRight = size;
        BottomLeft = size;
        BottomRight = size;
    }

    public BorderRadius(Size topLeft, Size topRight, Size bottomLeft, Size bottomRight)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomLeft = bottomLeft;
        BottomRight = bottomRight;
    }

    public override string ToString()
    {
        return $"border-radius: {TopLeft} {TopRight} {BottomLeft} {BottomRight};";
    }
}
