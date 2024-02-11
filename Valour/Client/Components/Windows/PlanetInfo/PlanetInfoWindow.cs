using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Client.Windows;

namespace Valour.Client.Components.Windows.PlanetInfo;

public class PlanetInfoWindow : ClientWindow, IPlanetWindow
{
    public Planet Planet { get; set; }
    private readonly string _lockKey = Guid.NewGuid().ToString();
    
    public override Type GetComponentType() =>
        typeof(PlanetInfoWindowComponent);

    public PlanetInfoWindow(Planet planet)
    {
        this.Planet = planet;
        ValourClient.AddPlanetLock(_lockKey, Planet.Id);
    }

    public override async Task OnClosedAsync()
    {
        await ValourClient.RemovePlanetLock(_lockKey);
    }
}