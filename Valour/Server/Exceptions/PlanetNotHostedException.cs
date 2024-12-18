namespace Valour.Server.Exceptions;

public class PlanetNotHostedException : Exception
{
    public PlanetNotHostedException(long planetId) : 
        base($"Planet with ID {planetId} is requested but not hosted.")
    {
    }
}