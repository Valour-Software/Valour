namespace Valour.Client.Device;

public class DeviceInfo
{   
    public static bool IsMobile { get; set; }
    public static bool IsEmbedded { get; set; }

    /// <summary>
    /// True in the iPhone and iPad app. App Store rules apply there, such as
    /// keeping NSFW content hidden unless the user turned it on elsewhere.
    /// </summary>
    public static bool IsIosApp { get; set; }
}
