using Valour.Sdk.Client;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

/// <summary>How an attempt to connect Google Drive ended.</summary>
public record DriveConnectOutcome(bool Cancelled, DriveConnectionInfo Info, string Error)
{
    public bool Success => Info is not null;
}

/// <summary>
/// Connects the account's Google Drive. This is a separate permission from
/// signing in with Google, so it is only asked for when someone chooses to turn
/// Drive on, from Attachments settings or the out-of-space message.
/// </summary>
public class GoogleDriveConnector
{
    private readonly ExternalSignInService _signIn;
    private readonly ValourClient _client;

    public GoogleDriveConnector(ExternalSignInService signIn, ValourClient client)
    {
        _signIn = signIn;
        _client = client;
    }

    /// <summary>
    /// Opens Google's consent page and finishes the connection. Call from a
    /// click handler without awaiting anything first, so the web popup is
    /// allowed to open.
    /// </summary>
    public async Task<DriveConnectOutcome> ConnectAsync()
    {
        var outcome = await _signIn.RunAsync(ExternalAuthProviders.Google, ExternalAuthIntent.ConnectDrive);

        if (outcome.Result is null)
            return new DriveConnectOutcome(true, null, null);

        if (outcome.IsError)
            return new DriveConnectOutcome(false, null, outcome.Message);

        if (outcome.Result != ExternalAuthResults.DriveConnected || string.IsNullOrEmpty(outcome.Ticket))
            return new DriveConnectOutcome(false, null, "Google Drive could not be connected.");

        var connected = await _client.AttachmentService.ConnectDriveAsync(outcome.Ticket, outcome.Verifier);
        return connected.Success
            ? new DriveConnectOutcome(false, connected.Data, null)
            : new DriveConnectOutcome(false, null, connected.Message);
    }
}
