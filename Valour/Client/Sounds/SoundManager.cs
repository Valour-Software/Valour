using Valour.Client.Components.Sounds;

namespace Valour.Client.Sounds;

public class SoundManager
{
    public SoundsComponent Component { get; set; }

    public async Task PlaySound(string name)
    {
        await Component.PlaySound(name);
    }
}
