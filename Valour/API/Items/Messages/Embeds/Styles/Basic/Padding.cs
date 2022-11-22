namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public struct Padding : IStyle
{
    public Size Left { get; set; }
    public Size Right { get; set; }
    public Size Top { get; set; }
    public Size Bottom { get; set; }

    public Padding(Size size)
    {
        Left = size;
        Right = size;
        Top = size;
        Bottom = size;
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
        return @$"padding-left: {Left};
                  padding-right: {Right};
                  padding-top: {Top};
                  padding-bottom: {Bottom};";
    }
}
