namespace Valour.Client.Utility;

public struct ElementDimensions
{
    public float Width { get; set; }
    public float Height { get; set; }
}

public struct ElementPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class ElementBounds
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Top { get; set; }
    public double Left { get; set; }
    public double Bottom { get; set; }
    public double Right { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public struct WindowUri
{
    public string Href { get; set; }
    public string Origin { get; set; }
    public string Protocol { get; set; }
    public string Host { get; set; }
    public string Hostname { get; set; }
    public string Port { get; set; }
    public string Pathname { get; set; }
    public string Search { get; set; }
    public string Hash { get; set; }
}

public struct VerticalContainerDistance
{
    public float TopDistance { get; set; }
    public float BottomDistance { get; set; }
}
