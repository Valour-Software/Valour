namespace Valour.Sdk.Models.Embeds.Styles;

public enum SizeUnit
{
    Zero,
    Auto,
    Pixels,
    Percent,
    MaxContent,
    MinContent,
    FitContent,
    Em,
}

/// <summary>
/// A CSS length value. Only used at build time; embeds serialize
/// the compiled CSS string.
/// </summary>
public readonly struct Size
{
    public static readonly Size Zero = new(SizeUnit.Zero);
    public static readonly Size Auto = new(SizeUnit.Auto);
    public static readonly Size Full = Percent(100);
    public static readonly Size Half = Percent(50);
    public static readonly Size Third = Percent(33);
    public static readonly Size Quarter = Percent(25);

    public SizeUnit Unit { get; }
    public double Value { get; }

    public Size(SizeUnit unit, double value = 0)
    {
        Unit = unit;
        Value = value;
    }

    public static Size Pixels(double value) => new(SizeUnit.Pixels, value);
    public static Size Percent(double value) => new(SizeUnit.Percent, value);
    public static Size Em(double value) => new(SizeUnit.Em, value);

    public override string ToString() => Unit switch
    {
        SizeUnit.Zero => "0",
        SizeUnit.Auto => "auto",
        SizeUnit.MaxContent => "max-content",
        SizeUnit.MinContent => "min-content",
        SizeUnit.FitContent => "fit-content",
        SizeUnit.Pixels => $"{Value}px",
        SizeUnit.Percent => $"{Value}%",
        SizeUnit.Em => $"{Value}em",
        _ => "0",
    };
}
