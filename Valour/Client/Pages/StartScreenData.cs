namespace Valour.Client.Pages;

public class StartScreenData
{
    public StartScreen? Start { get; set; }
    public long? StartPlanetId { get; set; }
    public long? StartChannelId { get; set; }
    public long? StartMessageId { get; set; }
}

public enum StartScreen
{
    PlanetChannel,
    DirectChannel,
}