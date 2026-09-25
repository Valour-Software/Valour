namespace Valour.Shared.Models;

public class RemoveMfaRequest
{
    public string Password { get; set; }

    /// <summary>
    /// A current code from the authenticator being removed. Required when the
    /// authenticator has finished setup.
    /// </summary>
    public string MultiFactorCode { get; set; }
}