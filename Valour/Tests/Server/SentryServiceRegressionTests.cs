using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Server.Cdn.Storage;
using Valour.Server.Services;

namespace Valour.Tests.Server;

public class SentryServiceRegressionTests
{
    [Fact]
    public void DiscordTemplateWithoutRolePositions_PreservesAuthorityOrder()
    {
        using var template = JsonDocument.Parse("""[{"id":0,"name":"@everyone"},{"id":1,"name":"member"},{"id":2,"name":"admin"}]""");
        var roles = DiscordImportService.OrderTemplateRoles(template.RootElement.EnumerateArray());
        Assert.Equal(new long[] { 2, 1, 0 }, roles.Select(x => x.GetProperty("id").GetInt64()));
    }

    [Fact]
    public void DiscordTemplateWithExplicitPositions_UsesPositions()
    {
        using var template = JsonDocument.Parse("""[{"id":1,"position":2},{"id":2,"position":1},{"id":0,"position":0}]""");
        var roles = DiscordImportService.OrderTemplateRoles(template.RootElement.EnumerateArray());
        Assert.Equal(new long[] { 1, 2, 0 }, roles.Select(x => x.GetProperty("id").GetInt64()));
    }

    [Theory]
    [InlineData("_content/Valour.Client/media/user-icons/icon-0.webp", "https://chat.example.test/_content/Valour.Client/media/user-icons/icon-0.webp")]
    [InlineData("/icon.png", "https://chat.example.test/icon.png")]
    [InlineData("https://cdn.example.test/icon.png", "https://cdn.example.test/icon.png")]
    [InlineData("data:image/png;base64,abc", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NotificationImages_AreAbsoluteWebUrls(string? input, string? expected)
    {
        Assert.Equal(expected, PushNotificationService.GetNotificationImageUrl(input, "https://chat.example.test"));
    }

    [Fact]
    public async Task UploadRetry_RewindsAndPreservesCallerStream()
    {
        using var client = new FailingOnceS3Client();
        var storage = new S3ObjectStorage(client, "test", NullLogger.Instance);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("avatar bytes"));
        var result = await storage.PutAsync("avatar.webp", stream, "image/webp");
        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { "avatar bytes", "avatar bytes" }, client.Payloads);
        Assert.True(stream.CanRead);
    }

    private sealed class FailingOnceS3Client : AmazonS3Client
    {
        public List<string> Payloads = [];
        public FailingOnceS3Client() : base(new AnonymousAWSCredentials(), new AmazonS3Config { ServiceURL = "http://localhost:1" }) { }
        public override async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(request.InputStream, leaveOpen: true);
            Payloads.Add(await reader.ReadToEndAsync(cancellationToken));
            if (request.AutoCloseStream) request.InputStream.Dispose();
            if (Payloads.Count == 1)
                throw new AmazonS3Exception("Transient storage failure") { StatusCode = HttpStatusCode.ServiceUnavailable };
            return new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK };
        }
    }
}
