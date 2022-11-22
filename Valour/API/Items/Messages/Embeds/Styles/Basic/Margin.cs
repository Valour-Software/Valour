namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class Margin : StyleBase
{
    public Size Left { get; set; }
    public Size Right { get; set; }
    public Size Top { get; set; }
    public Size Bottom { get; set; }

    public Margin(Size size)
    {
        Left = size;
        Right = size;
        Top = size;
        Bottom = size;
    }

    public Margin(Size left, Size right, Size top, Size bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public override string ToString()
    {
        return @$"margin-left: {Left};
                  margin-right: {Right};
                  margin-top: {Top};
                  margin-bottom: {Bottom};";
    }
}
