namespace Valour.Shared.Models;

public sealed class PlanetOwnershipTransferRequest
{
    public long NewOwnerUserId { get; set; }
    public string MultiFactorCode { get; set; }

    /// <summary>
    /// For an invite-only planet, the body of a membership log entry that
    /// transfers the log's ownership to the new owner, signed by the current
    /// owner's device. The server appends it together with the transfer.
    /// </summary>
    public byte[] AccessLogEntryBody { get; set; }

    /// <summary>The signature of <see cref="AccessLogEntryBody"/>.</summary>
    public byte[] AccessLogEntrySignature { get; set; }
}
