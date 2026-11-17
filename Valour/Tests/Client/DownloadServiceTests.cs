using System.Net;
using Valour.Client.Utility.Downloads;

namespace Valour.Tests.Client;

public class DownloadServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "valour-download-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    /// <summary>
    /// A host that saves to a temporary folder, standing in for a native one.
    /// </summary>
    private sealed class FolderDownloadService(string folder, HttpClient http) : StreamedDownloadService(http)
    {
        protected override Task<DownloadDestination> OpenDestinationAsync(
            string fileName, string mimeType, CancellationToken cancellationToken) =>
            Task.FromResult<DownloadDestination>(new FolderDownloadDestination(folder, fileName));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private FolderDownloadService CreateService(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new FolderDownloadService(_folder, new HttpClient(handler));
    }

    [Fact]
    public async Task DownloadAsync_WritesTheFileAndReportsProgress()
    {
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }, out _);

        DownloadProgress? last = null;
        var result = await service.DownloadAsync(
            new DownloadRequest("https://example.test/a.bin", "report.bin"),
            new Progress<DownloadProgress>(p => last = p));

        Assert.True(result.Success, result.Message);
        Assert.Equal(Path.Combine(_folder, "report.bin"), result.Location);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.Location));
        Assert.False(service.UsesBrowserDownloads);
        // Progress is reported on a timer, so only the final report is certain.
        await Task.Delay(50);
        Assert.Equal(bytes.Length, last?.BytesReceived);
    }

    [Fact]
    public async Task DownloadAsync_NeverReplacesAnExistingFile()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("new") }, out _);
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "notes.txt"), "old");

        var result = await service.DownloadAsync(new DownloadRequest("https://example.test/n", "notes.txt"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(Path.Combine(_folder, "notes (1).txt"), result.Location);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(_folder, "notes.txt")));
        Assert.Equal("new", await File.ReadAllTextAsync(result.Location));
    }

    [Fact]
    public async Task DownloadAsync_SendsRequestHeaders()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("x") }, out var handler);

        await service.DownloadAsync(new DownloadRequest(
            "https://example.test/h", "h.txt", Headers: new Dictionary<string, string> { ["Authorization"] = "Bearer abc" }));

        Assert.Equal("Bearer abc", handler.LastRequest.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task DownloadAsync_FailsWithoutLeavingAFileWhenTheServerRefuses()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.Gone), out _);

        var result = await service.DownloadAsync(new DownloadRequest("https://example.test/gone", "gone.txt"));

        Assert.False(result.Success);
        Assert.Contains("410", result.Message);
        Assert.False(Directory.Exists(_folder) && Directory.GetFiles(_folder).Length > 0);
    }

    [Fact]
    public async Task DownloadAsync_RemovesAFileThatEndedEarly()
    {
        // The server promises 1000 bytes and delivers 10.
        var service = CreateService(_ =>
        {
            var content = new ByteArrayContent(new byte[10]);
            content.Headers.ContentLength = 1000;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new TruncatedContent(content, 1000) };
        }, out _);

        var result = await service.DownloadAsync(new DownloadRequest("https://example.test/short", "short.bin"));

        Assert.False(result.Success);
        Assert.False(Directory.Exists(_folder) && Directory.GetFiles(_folder).Length > 0);
    }

    [Fact]
    public async Task DownloadAsync_CancellationRemovesThePartialFile()
    {
        using var cts = new CancellationTokenSource();
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new EndlessStream(cts)),
        }, out _);

        var result = await service.DownloadAsync(new DownloadRequest("https://example.test/big", "big.bin"), null, cts.Token);

        Assert.False(result.Success);
        Assert.False(Directory.Exists(_folder) && Directory.GetFiles(_folder).Length > 0);
    }

    [Theory]
    [InlineData("ftp://example.test/a")]
    [InlineData("/relative/path")]
    [InlineData("")]
    public async Task DownloadAsync_RejectsLinksThatAreNotHttp(string url)
    {
        var service = CreateService(_ => throw new InvalidOperationException("must not be called"), out _);

        var result = await service.DownloadAsync(new DownloadRequest(url, "a.txt"));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task SaveAsync_WritesInMemoryContent()
    {
        var service = CreateService(_ => throw new InvalidOperationException("must not be called"), out _);

        var result = await service.SaveAsync("event.ics", "text/calendar", "BEGIN:VCALENDAR"u8.ToArray());

        Assert.True(result.Success, result.Message);
        Assert.Equal("BEGIN:VCALENDAR", await File.ReadAllTextAsync(result.Location));
    }

    [Theory]
    [InlineData("photo.png", "photo.png")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Windows\\system32\\evil.exe", "evil.exe")]
    [InlineData("a<b>:c|d?.txt", "a_b__c_d_.txt")]
    [InlineData("  .hidden. ", "hidden")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("", "download")]
    [InlineData(null, "download")]
    [InlineData("...", "download")]
    public void Sanitize_ProducesAPlainFileName(string input, string expected)
    {
        Assert.Equal(expected, DownloadFileNames.Sanitize(input));
    }

    [Fact]
    public void Sanitize_ShortensLongNamesAndKeepsTheExtension()
    {
        var name = DownloadFileNames.Sanitize(new string('a', 400) + ".zip");

        Assert.True(name.Length <= 120);
        Assert.EndsWith(".zip", name);
    }

    /// <summary>
    /// Delivers a short body while the headers promise a longer one.
    /// </summary>
    private sealed class TruncatedContent(HttpContent inner, long promised) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => inner.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = promised;
            return true;
        }
    }

    /// <summary>
    /// Produces bytes forever, and cancels its token after a few reads.
    /// </summary>
    private sealed class EndlessStream(CancellationTokenSource cts) : Stream
    {
        private int _reads;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++_reads == 3)
                cts.Cancel();

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            buffer.Span[..Math.Min(buffer.Length, 1024)].Clear();
            return Math.Min(buffer.Length, 1024);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
