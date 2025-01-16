using Microsoft.AspNetCore.Mvc.Testing;
using Valour.Sdk.Utility;
using Valour.Server;

namespace Valour.Tests;

public class TestHttpProvider : HttpClientProvider
{
    private readonly WebApplicationFactory<Program> _factory;
    
    public TestHttpProvider(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }
    
    public HttpClient GetHttpClient()
    {
        return _factory.CreateClient();
    }
    
    public HttpMessageHandler GetHttpMessageHandler()
    {
        return _factory.Server.CreateHandler();
    }
}