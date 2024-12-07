namespace Valour.Client.Utility;

public struct ElementDimensions
{
    public float Width { get; set; }
    public float Height { get; set; }
}

public struct ElementPosition
{
    public float X { get; set; }
    public float Y { get; set; }
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