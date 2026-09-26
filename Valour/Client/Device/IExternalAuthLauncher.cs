using Valour.Shared.Models;

namespace Valour.Client.Device;

/// <summary>
/// Opens a sign-in provider's page and brings back the result. The web app
/// uses a browser popup; native shells use the system browser, which
/// providers require instead of an embedded web view.
/// </summary>
public interface IExternalAuthLauncher
{
    /// <summary>Where the server should send the result.</summary>
    ExternalAuthClient Client { get; }

    /// <summary>
    /// Prepares to receive a result. Call this directly in the click handler,
    /// before anything is awaited, so a browser treats its popup as opened by
    /// the person rather than blocking it.
    /// </summary>
    ExternalAuthLaunch Prepare();
}

/// <summary>One attempt to sign in with a provider.</summary>
public abstract class ExternalAuthLaunch : IAsyncDisposable
{
    /// <summary>The loopback port a desktop app listens on, if any.</summary>
    public virtual int? LoopbackPort => null;

    /// <summary>
    /// Opens the provider page and waits until the person finishes there.
    /// Returns null if they cancel or the wait is cancelled.
    /// </summary>
    public abstract Task<ExternalAuthResultResponse> WaitAsync(
        ExternalAuthBeginResponse begin, string verifier, CancellationToken cancellationToken);

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
