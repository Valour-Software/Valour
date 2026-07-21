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
}
