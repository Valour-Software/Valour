using System.Net;
using System.Text;
using Valour.Sdk.Client;
using Valour.Sdk.Services;

namespace Valour.Tests.Services;

public class KlipyServiceTests
{
    [Fact]
    public async Task SearchAsync_UsesThePublicClientEndpointAndNormalizesOnlyKlipyMedia()
    {
        var handler = new KlipyResponseHandler("""
            {
              "result": true,
              "data": {
                "data": [
                  {
                    "slug": "happy-dog",
                    "title": "Happy dog",
                    "file": {
                      "sm": { "webp": { "url": "https://static.klipy.com/preview.webp", "width": 120, "height": 100, "size": 12 } },
                      "md": { "gif": { "url": "https://static.klipy.com/happy.gif", "width": 480, "height": 400, "size": 1200 } }
                    }
                  },
                  {
                    "slug": "untrusted-host",
                    "title": "Do not render",
                    "file": {
                      "sm": { "webp": { "url": "https://example.invalid/preview.webp", "width": 120, "height": 100, "size": 12 } },
                      "md": { "gif": { "url": "https://example.invalid/bad.gif", "width": 480, "height": 400, "size": 1200 } }
                    }
                  }
                ],
                "has_next": true
              }
            }
            """);
        using var http = new HttpClient(handler);
        var client = new ValourClient("https://api.valour.example/");
        var service = new KlipyService(client, http);
        service.Configure("public-web-key");

        var result = await service.SearchAsync("happy dogs", 2);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Data.HasNext);
        var gif = Assert.Single(result.Data.Results);
        Assert.Equal("happy-dog", gif.Slug);
        Assert.Equal("https://static.klipy.com/happy.gif", gif.Gif!.Url);
        Assert.Equal(
            "https://api.klipy.com/api/v1/public-web-key/gifs/search?q=happy%20dogs&page=2&per_page=50",
            handler.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetCategoriesAsync_ParsesTheRealCategoriesShape()
    {
        // The live Klipy response nests categories as objects under data.categories
        // (not a bare string array), with preview_url on static.klipy.com.
        var handler = new KlipyResponseHandler("""
            {
              "result": true,
              "data": {
                "locale": "en_US",
                "categories": [
                  { "category": "happy birthday", "query": "happy birthday", "preview_url": "https://static.klipy.com/hb.gif" },
                  { "category": "bad host", "query": "bad", "preview_url": "https://example.invalid/x.gif" }
                ]
              }
            }
            """);
        using var http = new HttpClient(handler);
        var service = new KlipyService(new ValourClient("https://api.valour.example/"), http);
        service.Configure("public-web-key");

        var result = await service.GetCategoriesAsync();

        Assert.True(result.Success, result.Message);
        // The example.invalid preview is filtered by the host allowlist.
        var category = Assert.Single(result.Data);
        Assert.Equal("happy birthday", category.Name);
        Assert.Equal("happy birthday", category.SearchTerm);
        Assert.Equal("https://static.klipy.com/hb.gif", category.Image);
        Assert.Equal(
            "https://api.klipy.com/api/v1/public-web-key/gifs/categories",
            handler.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task RegisterShareAsync_DoesNotDiscloseTheValourUserId()
    {
        var handler = new KlipyResponseHandler("{ \"result\": true }");
        using var http = new HttpClient(handler);
        var client = new ValourClient("https://api.valour.example/");
        var service = new KlipyService(client, http);
        service.Configure("public-web-key");

        var result = await service.RegisterShareAsync("happy-dog");

        Assert.True(result.Success, result.Message);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Contains("\"customer_id\":\"valour\"", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_WithoutAConfiguredKey_DoesNotMakeANetworkRequest()
    {
        var handler = new KlipyResponseHandler("{}");
        using var http = new HttpClient(handler);
        var service = new KlipyService(new ValourClient("https://api.valour.example/"), http);

        var result = await service.SearchAsync("happy");

        Assert.False(result.Success);
        Assert.Equal(503, result.Code);
        Assert.Null(handler.RequestUri);
    }

    private sealed class KlipyResponseHandler(string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
