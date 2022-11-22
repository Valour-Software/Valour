namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public struct Position : IStyle
{
    public Size Left {  get; set; }
    public Size Right { get; set; }
    public Size Top {  get; set; }
    public Size Bottom {  get; set; }

    public Position(Size left, Size right, Size top, Size bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    public override string ToString()
    {
        return @$"left: {Left};
                  right: {Right};
                  top: {Top};
                  bottom: {Bottom};";
    }
}
