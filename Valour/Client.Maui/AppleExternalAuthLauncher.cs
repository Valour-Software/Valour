#if IOS || MACCATALYST
using Valour.Client.Device;
using Valour.Shared.Models;

namespace Valour.Client.Maui;

/// <summary>
/// Signs in with a provider in an ASWebAuthenticationSession, the system
/// browser sheet Apple provides for this. Providers refuse sign-in inside the
/// app's own web view. The server sends the result to gg.valour.app://auth,
/// the same address the Android app uses, and the session catches that
/// address itself, so the app doesn't need to register the scheme.
/// </summary>
public class AppleExternalAuthLauncher : IExternalAuthLauncher
{
    private const string CallbackUrl = "gg.valour.app://auth";

    public ExternalAuthClient Client => ExternalAuthClient.Android;

    public ExternalAuthLaunch Prepare() => new Launch();

    private sealed class Launch : ExternalAuthLaunch
    {
        public override async Task<ExternalAuthResultResponse> WaitAsync(
            ExternalAuthBeginResponse begin, string verifier, CancellationToken cancellationToken)
        {
            try
            {
                var result = await MainThread.InvokeOnMainThreadAsync(() =>
                    WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
                    {
                        Url = new Uri(begin.AuthorizationUrl),
                        CallbackUrl = new Uri(CallbackUrl),
                    }));

                return new ExternalAuthResultResponse
                {
                    Result = result.Properties.GetValueOrDefault("result"),
                    Ticket = result.Properties.GetValueOrDefault("ticket"),
                    Message = result.Properties.GetValueOrDefault("message"),
                };
            }
            catch (Exception e) when (e is OperationCanceledException or TaskCanceledException)
            {
                // The person closed the sign-in sheet.
                return null;
            }
        }
    }
}
#endif
