using Valour.Client.Components.Utility;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class PlanetIconTests
{
    [Fact]
    public void DefaultFormat_SupportsExistingAnimatedPlanetIcons()
    {
        var component = new PlanetIcon();

        Assert.Equal(IconFormat.WebpAnimated64, component.Format);
    }
}
