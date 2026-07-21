using Valour.Server.Cdn.Api;

namespace Valour.Tests.Cdn;

public class UploadApiTests
{
    [Theory]
    [InlineData("https://cdn.example/profile.webp", "https://cdn.example/profile.webp?v=42")]
    [InlineData("https://cdn.example/profile.webp?size=300", "https://cdn.example/profile.webp?size=300&v=42")]
    public void AddCacheVersion_ProducesANewBrowserCacheKey(string url, string expected)
    {
        Assert.Equal(expected, UploadApi.AddCacheVersion(url, 42));
    }
}
