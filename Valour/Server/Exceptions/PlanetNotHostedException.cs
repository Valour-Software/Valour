namespace Valour.Server.Exceptions;

public class PlanetNotHostedException : Exception
{
    public string CorrectNode { get; }
    public long PlanetId { get; }
    
    public PlanetNotHostedException(long planetId, string correctNode) : base($"Planet with ID {planetId} is requested but not hosted.")
    {
        CorrectNode = correctNode;
        PlanetId = planetId;
    }
} 