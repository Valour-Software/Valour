namespace Valour.Shared.Models;

public sealed class PlanetOwnershipTransferRequest
{
    public long NewOwnerUserId { get; set; }
    public string MultiFactorCode { get; set; }
}
