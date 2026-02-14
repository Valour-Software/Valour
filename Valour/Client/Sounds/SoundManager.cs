using Valour.Client.Components.Sounds;
using Valour.Shared.Models;

namespace Valour.Client.Sounds;

public class SoundManager
{
    public SoundsComponent? Component { get; set; }
    public int NotificationVolume { get; private set; } = NotificationPreferences.DefaultNotificationVolume;

    public void SetNotificationVolume(int volume)
    {
        NotificationVolume = NotificationPreferences.ClampVolume(volume);
    }

    public async Task PlaySound(string name)
    {
        if (Component is null)
            return;

        var volume = NotificationVolume / 100d;
        await Component.PlaySound(name, volume);
    }
}
