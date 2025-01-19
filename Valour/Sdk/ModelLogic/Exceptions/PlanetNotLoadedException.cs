using Valour.Shared.Models;

namespace Valour.Sdk.ModelLogic.Exceptions;

public class PlanetNotLoadedException : Exception
{  
    public PlanetNotLoadedException(long planetId, object requestedBy)
        : base($"Tried to access planet {planetId} but it was not loaded. Requested by {requestedBy.GetType()}")
    {
    }
}