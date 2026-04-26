namespace Valour.Config.Configs;

public class MediaSafetyConfig
{
    public static MediaSafetyConfig Current { get; private set; } = null!;

    public MediaSafetyConfig()
    {
        Current = this;
    }

    public bool Enabled { get; set; }
    public string Mode { get; set; } = "Shadow";
    public bool FailClosed { get; set; }
    public bool HashMatchImageUploads { get; set; } = true;
    public string Provider { get; set; } = "PhotoDNA";
    public string PhotoDnaEndpoint { get; set; } = string.Empty;
    public string PhotoDnaSubscriptionKey { get; set; } = string.Empty;
    public string PhotoDnaHeaderName { get; set; } = "Ocp-Apim-Subscription-Key";
    public int TimeoutSeconds { get; set; } = 10;
}
