namespace Valour.Sdk.Utility;

public interface HttpClientProvider
{
    public HttpClient GetHttpClient();
    public HttpMessageHandler GetHttpMessageHandler();
}