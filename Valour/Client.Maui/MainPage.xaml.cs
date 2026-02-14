namespace Valour.Client.Maui;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        blazorWebView.BlazorWebViewInitialized += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine("BlazorWebView initialized");
        };
        blazorWebView.UrlLoading += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"URL loading: {e.Url}");
        };
    }
}
