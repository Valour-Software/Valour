namespace Valour.Client.Device;

public class DeviceInfo
{   
    /// <summary>
    /// True when the app shows the phone layout: one window at a time and a
    /// slide-out sidebar. The iPhone and iPad app can switch layouts while it
    /// runs (see <see cref="SetMobileLayout"/>); elsewhere it is set once.
    /// </summary>
    public static bool IsMobile { get; set; }

    /// <summary>
    /// Raised after <see cref="IsMobile"/> changes while the app runs, with the
    /// new value. The page shell rebuilds the layout when it fires.
    /// </summary>
    public static event Action<bool> LayoutModeChanged;

    /// <summary>
    /// Switches between the phone and wide layouts, such as when a foldable
    /// iPhone opens or closes or an iPad enters Split View.
    /// </summary>
    public static void SetMobileLayout(bool mobile)
    {
        if (IsMobile == mobile)
            return;

        IsMobile = mobile;
        LayoutModeChanged?.Invoke(mobile);
    }
    public static bool IsEmbedded { get; set; }

    /// <summary>
    /// True in the iPhone and iPad app. App Store rules apply there, such as
    /// keeping NSFW content hidden unless the user turned it on elsewhere.
    /// </summary>
    public static bool IsIosApp { get; set; }
}
