using Microsoft.Maui.ApplicationModel;
using Valour.Client;

namespace Valour.Client.Maui;

public sealed class MauiNativeWindowService : INativeWindowService
{
    public bool SupportsTabPopout => OperatingSystem.IsWindows() || OperatingSystem.IsMacCatalyst();

    public Task<bool> TryOpenTabPopoutWindow(string popoutKey, string title)
    {
        if (!SupportsTabPopout || string.IsNullOrWhiteSpace(popoutKey))
            return Task.FromResult(false);

        var app = Application.Current;
        if (app is null)
            return Task.FromResult(false);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var startPath = $"/popout/{Uri.EscapeDataString(popoutKey)}";
            var page = new MainPage(startPath);
            var window = new Window(page)
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Valour" : $"Valour - {title}"
            };

            app.OpenWindow(window);
        });

        return Task.FromResult(true);
    }
}
