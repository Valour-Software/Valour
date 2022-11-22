namespace Valour.Api.Items.Messages.Embeds.Styles;

public enum Unit
{
    Zero,
    Auto,
    Pixels,
    Percent,
    MaxContent,
    MinContent,
    FitContent,
    Em
}

public struct Size
{
    public static readonly Size Zero = new Size(Unit.Zero);
    public static readonly Size Full = new Size(Unit.Percent, 100);
    public static readonly Size Half = new Size(Unit.Percent, 50);
    public static readonly Size Third = new Size(Unit.Percent, 33);
    public static readonly Size Quarter = new Size(Unit.Percent, 25);

    public Unit Type { get; set; }
    public int Value { get; set; }

    public Size(Unit type, int size = 0)
    {
        Type = type;
        Value = size;
    }

    public override string ToString()
    {
        switch (Type)
        {
            case Unit.Zero:
                return "0";
            case Unit.Auto:
                return "auto";
            case Unit.MaxContent:
                return "max-content";
            case Unit.MinContent:
                return "min-content";
            case Unit.FitContent:
                return "fit-content";
            case Unit.Pixels:
                return Value + "px";
            case Unit.Percent:
                return Value + "%";
            case Unit.Em:
                return Value + "em";
        }

        return "";
    }

}
