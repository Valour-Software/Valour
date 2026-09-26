using Valour.Client.Device;
using Valour.Sdk.Services;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

/// <summary>
/// How the person confirmed who they are for a sensitive change: their
/// password, or a proof from a linked account or fingerprint.
/// </summary>
public record IdentityConfirmation(string Password, string ReauthProof);

/// <summary>The result of one provider sign-in, with the verifier needed to redeem its ticket.</summary>
public record ExternalSignInOutcome(string Result, string Ticket, string Message, string Verifier, string Provider)
{
    public bool IsError => Result is null or ExternalAuthResults.Error;
}

/// <summary>
/// Runs sign-ins with Google and Discord for the login page, the register
/// page, and Connections settings, and carries a pending sign-up or two-factor
/// step between pages.
/// </summary>
public class ExternalSignInService
{
    private static readonly Dictionary<string, (string Name, string Icon)> Providers = new()
    {
        [ExternalAuthProviders.Google] = ("Google", "bi-google"),
        [ExternalAuthProviders.Discord] = ("Discord", "bi-discord"),
    };

    private readonly AuthService _authService;
    private readonly IExternalAuthLauncher _launcher;
    private List<string> _available;

    public ExternalSignInService(AuthService authService, IExternalAuthLauncher launcher)
    {
        _authService = authService;
        _launcher = launcher;
    }

    /// <summary>A provider sign-up waiting for the register page to finish it.</summary>
    public ExternalSignInOutcome PendingRegistration { get; set; }

    /// <summary>A provider sign-in waiting for a two-factor code on the login page.</summary>
    public ExternalSignInOutcome PendingLogin { get; set; }

    // The server accepts a proof for five minutes. Stop reusing it a little
    // earlier so a change isn't refused partway through.
    private static readonly TimeSpan ReauthReuseWindow = TimeSpan.FromMinutes(4);
    private string _reauthProof;
    private DateTime _reauthProofExpires;

    /// <summary>Keeps a fresh identity proof so several changes can use it.</summary>
    public void RememberReauthProof(string proof)
    {
        _reauthProof = proof;
        _reauthProofExpires = DateTime.UtcNow + ReauthReuseWindow;
    }

    /// <summary>A recent identity proof, or null when there isn't one.</summary>
    public string GetReauthProof() =>
        _reauthProof is not null && DateTime.UtcNow < _reauthProofExpires ? _reauthProof : null;

    public void ForgetReauthProof() => _reauthProof = null;

    public static string GetName(string provider) =>
        Providers.TryGetValue(provider ?? string.Empty, out var info) ? info.Name : provider;

    public static string GetIcon(string provider) =>
        Providers.TryGetValue(provider ?? string.Empty, out var info) ? info.Icon : "bi-box-arrow-in-right";

    /// <summary>The provider matching a credential type, such as "Google" to "google".</summary>
    public static string FromCredentialType(string credentialType) =>
        Providers.Keys.FirstOrDefault(x => string.Equals(GetName(x), credentialType, StringComparison.OrdinalIgnoreCase));

    /// <summary>Providers the server offers, in display order.</summary>
    public async Task<List<string>> GetProvidersAsync()
    {
        if (_available is null)
        {
            var offered = await _authService.GetExternalProvidersAsync();
            _available = Providers.Keys.Where(offered.Contains).ToList();
        }
        return _available;
    }

    /// <summary>
    /// Signs in with a provider. Call from a click handler without awaiting
    /// anything first, so the web popup is allowed to open.
    /// </summary>
    public async Task<ExternalSignInOutcome> RunAsync(
        string provider,
        ExternalAuthIntent intent,
        string reauthProof = null,
        CancellationToken cancellationToken = default)
    {
        var verifier = AuthService.CreateExternalAuthVerifier();
        await using var launch = _launcher.Prepare();

        var begin = await _authService.BeginExternalAuthAsync(provider, new ExternalAuthBeginRequest
        {
            Intent = intent,
            Client = _launcher.Client,
            LoopbackPort = launch.LoopbackPort,
            VerifierHash = AuthService.HashExternalAuthVerifier(verifier),
            ReauthProof = reauthProof,
        });

        if (!begin.Success)
            return new ExternalSignInOutcome(ExternalAuthResults.Error, null, begin.Message, verifier, provider);

        var result = await launch.WaitAsync(begin.Data, verifier, cancellationToken);
        if (result is null)
            return new ExternalSignInOutcome(null, null, "Sign-in was cancelled.", verifier, provider);

        return new ExternalSignInOutcome(result.Result, result.Ticket, result.Message, verifier, provider);
    }
}
