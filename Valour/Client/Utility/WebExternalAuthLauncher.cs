using Microsoft.JSInterop;
using Valour.Client.Device;
using Valour.Sdk.Services;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

/// <summary>
/// Signs in with a provider in a browser popup. Provider pages can cut the
/// popup's link to this window, so instead of waiting for a message the app
/// asks the server for the result until it arrives.
/// </summary>
public class WebExternalAuthLauncher : IExternalAuthLauncher
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);

    private readonly IJSRuntime _js;
    private readonly AuthService _authService;

    public WebExternalAuthLauncher(IJSRuntime js, AuthService authService)
    {
        _js = js;
        _authService = authService;
    }

    public ExternalAuthClient Client => ExternalAuthClient.Web;

    public ExternalAuthLaunch Prepare()
    {
        // Browsers only allow a popup that opens during the click itself. The
        // in-process runtime (WebAssembly) opens it synchronously, before the
        // sign-in address is known, and it is pointed there afterwards.
        var opened = _js is IJSInProcessRuntime inProcess && inProcess.Invoke<bool>("valourExternalAuth.open");
        return new Launch(this, opened);
    }

    private sealed class Launch(WebExternalAuthLauncher owner, bool opened) : ExternalAuthLaunch
    {
        public override async Task<ExternalAuthResultResponse> WaitAsync(
            ExternalAuthBeginResponse begin, string verifier, CancellationToken cancellationToken)
        {
            var shown = opened
                ? await owner._js.InvokeAsync<bool>("valourExternalAuth.navigate", begin.AuthorizationUrl)
                : await owner._js.InvokeAsync<bool>("valourExternalAuth.openUrl", begin.AuthorizationUrl);

            if (!shown)
                return new ExternalAuthResultResponse
                {
                    Result = ExternalAuthResults.Error,
                    Message = "Your browser blocked the sign-in window. Allow pop-ups for Valour and try again.",
                };

            var deadline = DateTime.UtcNow + MaxWait;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    await Task.Delay(PollInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                var result = await owner._authService.GetExternalResultAsync(begin.FlowId, verifier);
                if (result.Success && result.Data is not null)
                    return result.Data;
            }

            return null;
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await owner._js.InvokeVoidAsync("valourExternalAuth.close");
            }
            catch (JSException)
            {
                // The page may already be gone.
            }
        }
    }
}
