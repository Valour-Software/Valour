using Android.App;
using Android.Content;
using Android.Content.PM;
using Valour.Client.Device;
using Valour.Shared.Models;

namespace Valour.Client.Maui;

/// <summary>
/// Signs in with a provider in a Chrome Custom Tab. Google refuses sign-in in
/// embedded web views, so the app's own WebView can't be used. The server
/// sends the result to gg.valour.app://auth, which
/// <see cref="ExternalAuthCallbackActivity"/> receives.
/// </summary>
public class AndroidExternalAuthLauncher : IExternalAuthLauncher
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
            catch (OperationCanceledException)
            {
                // The person closed the browser tab.
                return null;
            }
        }
    }
}

/// <summary>Receives provider sign-in results from the system browser.</summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "gg.valour.app",
    DataHost = "auth")]
public class ExternalAuthCallbackActivity : WebAuthenticatorCallbackActivity
{
}
