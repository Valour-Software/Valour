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

    [Theory]
    [InlineData(new byte[] { (byte)'w', (byte)'O', (byte)'F', (byte)'2', 0 }, true)]
    [InlineData(new byte[] { (byte)'w', (byte)'O', (byte)'F', (byte)'F' }, true)]
    [InlineData(new byte[] { (byte)'O', (byte)'T', (byte)'T', (byte)'O' }, true)]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00 }, true)]
    [InlineData(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, false)] // an image renamed to .woff2
    [InlineData(new byte[] { (byte)'w', (byte)'O' }, false)]
    public void HasFontSignature_AcceptsOnlyFontFiles(byte[] header, bool expected)
    {
        Assert.Equal(expected, UploadApi.HasFontSignature(header));
    }
}
