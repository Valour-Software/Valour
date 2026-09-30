using Valour.Shared.Cdn;

namespace Valour.Tests.Cdn;

public class CdnUtilsTests
{
    [Theory]
    [InlineData("setup.exe", "application/octet-stream")]
    [InlineData("SETUP.EXE", "application/octet-stream")]
    [InlineData("setup.exe. ", "application/octet-stream")]
    [InlineData("setup.exe::$DATA", "application/octet-stream")]
    [InlineData("installer", "application/x-msdownload")]
    [InlineData("script.ps1", "text/plain")]
    [InlineData("application.apk", "application/zip")]
    public void IsExecutableUpload_BlocksExecutableNamesAndMimeTypes(string fileName, string mimeType)
    {
        Assert.True(CdnUtils.IsExecutableUpload(fileName, mimeType));
    }

    [Theory]
    [InlineData(new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00 })]
    [InlineData(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' })]
    [InlineData(new byte[] { 0xfe, 0xed, 0xfa, 0xcf })]
    public void IsExecutableUpload_BlocksRenamedNativeExecutable(byte[] header)
    {
        Assert.True(CdnUtils.IsExecutableUpload("notes.txt", "text/plain", header));
    }

    [Theory]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("source.cs", "text/plain; charset=utf-8")]
    [InlineData("archive.zip", "application/zip")]
    public void IsExecutableUpload_AllowsNonExecutableAttachments(string fileName, string mimeType)
    {
        Assert.False(CdnUtils.IsExecutableUpload(fileName, mimeType, "%PDF"u8));
    }

    [Theory]
    [InlineData("see https://www.tiktok.com/@scout2015/video/6718335390845095173 here", "https://www.tiktok.com/@scout2015/video/6718335390845095173")]
    [InlineData("https://www.threads.com/@mosseri/post/DFBmq7ySDvi", "https://www.threads.com/@mosseri/post/DFBmq7ySDvi")]
    [InlineData("home: https://example.com/~user/page", "https://example.com/~user/page")]
    public void UrlRegex_KeepsAtSignsAndTildesInPaths(string content, string expected)
    {
        var match = CdnUtils.UrlRegex.Match(content);

        Assert.True(match.Success);
        Assert.Equal(expected, match.Value);
    }
}
