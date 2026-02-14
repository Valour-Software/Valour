namespace Valour.Client.Maui;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        blazorWebView.BlazorWebViewInitialized += OnBlazorWebViewInitialized;
        blazorWebView.UrlLoading += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"URL loading: {e.Url}");
        };
    }

    private void OnBlazorWebViewInitialized(object? sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("BlazorWebView initialized");

#if ANDROID
        if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView webView)
        {
            webView.Settings.MediaPlaybackRequiresUserGesture = false;
            webView.SetWebChromeClient(new AudioPermissionChromeClient());
        }
#endif
    }
}
