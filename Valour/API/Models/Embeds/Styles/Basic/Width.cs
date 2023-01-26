using System.Text.Json.Serialization;

namespace Valour.Api.Items.Messages.Embeds.Styles.Basic;

public class Width : StyleBase
{
    public static readonly Width Zero = new Width(Size.Zero);
    public static readonly Width Full = new Width(Size.Full);
    public static readonly Width Half = new Width(Size.Half);
    public static readonly Width Third = new Width(Size.Third);
    public static readonly Width Quarter = new Width(Size.Quarter);

    [JsonPropertyName("s")]
    public Size Size { get; set; }

    public Width(Size size)
    {
        Size = size;
    }

    public override string ToString()
    {
        return $"width: {Size};";
    }
}

public class MaxWidth : StyleBase
{
    public static readonly MaxWidth Zero = new MaxWidth(Size.Zero);
    public static readonly MaxWidth Full = new MaxWidth(Size.Full);
    public static readonly MaxWidth Half = new MaxWidth(Size.Half);
    public static readonly MaxWidth Third = new MaxWidth(Size.Third);
    public static readonly MaxWidth Quarter = new MaxWidth(Size.Quarter);

    [JsonPropertyName("s")]
    public Size Size { get; set; }

    public MaxWidth(Size size)
    {
        Size = size;
    }

    public override string ToString()
    {
        return $"max-width: {Size};";
    }
}

public class MinWidth : StyleBase
{
    public static readonly MinWidth Zero = new MinWidth(Size.Zero);
    public static readonly MinWidth Full = new MinWidth(Size.Full);
    public static readonly MinWidth Half = new MinWidth(Size.Half);
    public static readonly MinWidth Third = new MinWidth(Size.Third);
    public static readonly MinWidth Quarter = new MinWidth(Size.Quarter);

    [JsonPropertyName("s")]
    public Size Size { get; set; }

    public MinWidth(Size size)
    {
        Size = size;
    }

    public override string ToString()
    {
        return $"min-width: {Size};";
    }
}