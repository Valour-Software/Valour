using Valour.Server.Cdn;

namespace Valour.Tests.Cdn;

public class CdnServePolicyTests
{
    [Theory]
    [InlineData("photo.png", "image/png", "image/png")]
    [InlineData("clip.mp4", "video/mp4; codecs=avc1", "video/mp4")]
    [InlineData("song.mp3", "audio/mpeg", "audio/mpeg")]
    public void Resolve_ServesNonScriptableMediaInline(string fileName, string mimeType, string expectedType)
    {
        var headers = CdnServePolicy.Resolve(fileName, mimeType);

        Assert.True(headers.Inline);
        Assert.Equal(expectedType, headers.ContentType);
        Assert.StartsWith("inline", headers.ContentDisposition);
    }

    [Theory]
    // An empty stored name produced no Content-Disposition at all before.
    [InlineData("", "unknown/unknown")]
    [InlineData("   ", "text/html")]
    [InlineData("a", "not a mime type")]
    [InlineData("page.html", "image/png")]
    [InlineData("image.svg", "image/svg+xml")]
    [InlineData("doc.xml", "application/xml")]
    [InlineData(null, null)]
    public void Resolve_ForcesOpaqueDownloadForUnknownOrActiveContent(string fileName, string mimeType)
    {
        var headers = CdnServePolicy.Resolve(fileName, mimeType);

        Assert.False(headers.Inline);
        Assert.Equal(CdnServePolicy.OpaqueContentType, headers.ContentType);
        Assert.StartsWith("attachment", headers.ContentDisposition);
        Assert.Contains("filename=", headers.ContentDisposition);
    }

    [Theory]
    [InlineData("report.pdf", "application/pdf")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("archive.zip", "application/zip")]
    public void Resolve_DownloadsOtherKnownTypes(string fileName, string mimeType)
    {
        var headers = CdnServePolicy.Resolve(fileName, mimeType);

        Assert.False(headers.Inline);
        Assert.Equal(mimeType, headers.ContentType);
        Assert.StartsWith("attachment", headers.ContentDisposition);
    }

    [Theory]
    [InlineData("", "file")]
    [InlineData("  ", "file")]
    [InlineData("..", "file")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\temp\\x.txt", "x.txt")]
    [InlineData("a\"b\r\nc.txt", "abc.txt")]
    public void NormalizeFileName_StripsPathsAndHeaderBreakingCharacters(string input, string expected)
    {
        Assert.Equal(expected, CdnServePolicy.NormalizeFileName(input));
    }

    [Theory]
    [InlineData("Image/PNG", "image/png")]
    [InlineData("text/plain; charset=utf-8", "text/plain")]
    [InlineData("unknown/unknown", null)]
    [InlineData("text/html\r\nX-Evil: 1", null)]
    [InlineData("", null)]
    public void NormalizeMimeType_AcceptsOnlyWellFormedRegisteredTypes(string input, string expected)
    {
        Assert.Equal(expected, CdnServePolicy.NormalizeMimeType(input));
    }
}
