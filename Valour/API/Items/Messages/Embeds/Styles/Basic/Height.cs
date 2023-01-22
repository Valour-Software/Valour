using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class Height : StyleBase
{
    public static readonly Height Zero = new Height(Size.Zero);
    public static readonly Height Full = new Height(Size.Full);
    public static readonly Height Half = new Height(Size.Half);
    public static readonly Height Third = new Height(Size.Third);
    public static readonly Height Quarter = new Height(Size.Quarter);

    [JsonPropertyName("s")]
    public Size Size { get; set; }

    internal Height() { }

    public Height(Size size)
    {
        Size = size;
    }

    public override string ToString()
    {
        return $"height: {Size};";
    }
}

public class MaxHeight : StyleBase
{
    public static readonly MaxHeight Zero = new MaxHeight(Size.Zero);
    public static readonly MaxHeight Full = new MaxHeight(Size.Full);
    public static readonly MaxHeight Half = new MaxHeight(Size.Half);
    public static readonly MaxHeight Third = new MaxHeight(Size.Third);
    public static readonly MaxHeight Quarter = new MaxHeight(Size.Quarter);

    [JsonPropertyName("s")]
    public Size Size { get; set; }

    internal MaxHeight() { }

    public MaxHeight(Size size)
    {
        Size = size;
    }

    public override string ToString()
    {
        return $"max-height: {Size};";
    }
}

public class MinHeight : StyleBase
{
    public static readonly MinHeight Zero = new MinHeight(Size.Zero);
    public static readonly MinHeight Full = new MinHeight(Size.Full);
    public static readonly MinHeight Half = new MinHeight(Size.Half);
    public static readonly MinHeight Third = new MinHeight(Size.Third);
    public static readonly MinHeight Quarter = new MinHeight(Size.Quarter);

    [JsonPropertyName("s")]
    public Size Size { get; set; }

    internal MinHeight() { }

    public MinHeight(Size size)
    {
        Size = size;
    }

    public override string ToString()
    {
        return $"min-height: {Size};";
    }
}
