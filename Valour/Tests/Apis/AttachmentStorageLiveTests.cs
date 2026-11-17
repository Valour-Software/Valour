using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Client.Utility.Downloads;
using Valour.Database;
using Valour.Database.Context;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

/// <summary>
/// End-to-end tests for the attachment storage quota and the Cleanup flow
/// against the real server: usage totals, the full-account answer, the backup
/// archive, and the rule that nothing is deleted before its backup finished.
/// </summary>
[Collection("ApiCollection")]
public class AttachmentStorageLiveTests
{
    private readonly LoginTestFixture _fixture;

    public AttachmentStorageLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<string> UploadTextAsync(string name, int length)
    {
        // Distinct content per file, because identical files are stored once.
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)('a' + bytes[i] % 26);

        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("text/plain");
        form.Add(content, "file", name);

        var response = await _fixture.Client.Http.PostAsync("upload/file", form);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpResponseMessage> TryUploadTextAsync(string name, int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)('a' + bytes[i] % 26);

        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("text/plain");
        form.Add(content, "file", name);
        return await _fixture.Client.Http.PostAsync("upload/file", form);
    }

    [Fact]
    public async Task FreeAccountHasFiveGibAndFullAccountsAreTurnedAway()
    {
        var attachments = _fixture.Client.AttachmentService;
        var userId = _fixture.Client.Me.Id;

        var before = await attachments.GetStorageAsync();
        Assert.True(before.Success, before.Message);
        Assert.Equal(5L * 1024 * 1024 * 1024, before.Data.QuotaBytes);

        await UploadTextAsync("quota-a.txt", 4000);
        var after = await attachments.GetStorageAsync();
        Assert.Equal(before.Data.UsedBytes + 4000, after.Data.UsedBytes);

        // Fill the account with records that stand in for large files.
        var fillerIds = new List<string>();
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            for (var i = 0; i < 3; i++)
            {
                var id = $"File/{userId}/quota-filler-{Guid.NewGuid():N}";
                fillerIds.Add(id);
                db.CdnBucketItems.Add(new CdnBucketItem
                {
                    Id = id,
                    Hash = id.Split('/')[^1],
                    UserId = userId,
                    MimeType = "application/octet-stream",
                    FileName = "filler.bin",
                    Category = ContentCategory.File,
                    SizeBytes = 1_900_000_000,
                    CreatedAt = DateTime.UtcNow,
                    Sha256Hash = new string('0', 64),
                });
            }
            await db.SaveChangesAsync();
        }

        try
        {
            var full = await attachments.GetStorageAsync();
            Assert.True(full.Data.IsFull);

            var response = await TryUploadTextAsync("quota-b.txt", 4000);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

            var info = JsonSerializer.Deserialize<UserStorageInfo>(
                await response.Content.ReadAsStringAsync(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(UserStorageInfo.StorageFullCode, info!.Code);
            Assert.Equal(full.Data.QuotaBytes, info.QuotaBytes);
            Assert.True(info.RequestedBytes > 0);
        }
        finally
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            await db.CdnBucketItems.Where(x => fillerIds.Contains(x.Id)).ExecuteDeleteAsync();
        }

        // Room is back, so uploads work again.
        await UploadTextAsync("quota-c.txt", 4000);
    }

    [Fact]
    public async Task CleanupDeletesOnlyAfterTheBackupArchiveFinished()
    {
        var attachments = _fixture.Client.AttachmentService;
        var marker = Guid.NewGuid().ToString("N")[..8];

        await UploadTextAsync($"cleanup-{marker}-1.txt", 3000);
        await UploadTextAsync($"cleanup-{marker}-2.txt", 3000);

        var criteria = new AttachmentCleanupCriteria
        {
            AsOf = DateTime.UtcNow.AddMinutes(1),
            Scope = AttachmentCleanupScope.All,
            Categories = [ContentCategory.File],
        };

        var preview = await attachments.PreviewCleanupAsync(criteria);
        Assert.True(preview.Success, preview.Message);
        var expectedCount = preview.Data.Count;
        Assert.True(expectedCount >= 2);

        // No backup, no deletion.
        var refused = await attachments.DeleteCleanupBatchAsync(new AttachmentCleanupDeleteRequest { Criteria = criteria });
        Assert.False(refused.Success);

        var unfinished = await attachments.StartCleanupArchiveAsync(criteria);
        Assert.True(unfinished.Success, unfinished.Message);
        var stillRefused = await attachments.DeleteCleanupBatchAsync(new AttachmentCleanupDeleteRequest
        {
            Criteria = criteria,
            ArchiveId = unfinished.Data.Id,
        });
        Assert.False(stillRefused.Success);

        // Download a full archive.
        var ticket = await attachments.StartCleanupArchiveAsync(criteria);
        Assert.True(ticket.Success, ticket.Message);
        Assert.Equal(expectedCount, ticket.Data.Count);

        using var anonymous = _fixture.Factory.CreateClient();
        var download = await anonymous.GetAsync(ticket.Data.DownloadPath);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);

        var zipBytes = await download.Content.ReadAsByteArrayAsync();
        using (var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
        {
            Assert.Contains(zip.Entries, e => e.FullName == "manifest.json");
            Assert.True(zip.Entries.Count(e => e.FullName != "manifest.json") >= expectedCount);
        }

        var status = await attachments.GetCleanupArchiveStatusAsync(ticket.Data.Id);
        Assert.Equal(AttachmentArchiveState.Completed, status.Data.State);
        Assert.Equal(0, status.Data.SkippedCount);

        // The archive link works once.
        var replay = await anonymous.GetAsync(ticket.Data.DownloadPath);
        Assert.False(replay.IsSuccessStatusCode);

        // A backup made for other criteria does not unlock this deletion.
        var otherCriteria = new AttachmentCleanupCriteria
        {
            AsOf = criteria.AsOf,
            Scope = AttachmentCleanupScope.All,
            Categories = [ContentCategory.Image],
        };
        var mismatch = await attachments.DeleteCleanupBatchAsync(new AttachmentCleanupDeleteRequest
        {
            Criteria = otherCriteria,
            ArchiveId = ticket.Data.Id,
        });
        Assert.False(mismatch.Success);

        var deleted = 0;
        while (true)
        {
            var batch = await attachments.DeleteCleanupBatchAsync(new AttachmentCleanupDeleteRequest
            {
                Criteria = criteria,
                ArchiveId = ticket.Data.Id,
            });
            Assert.True(batch.Success, batch.Message);
            deleted += batch.Data.Deleted;
            if (batch.Data.Remaining <= 0 || batch.Data.Deleted == 0)
                break;
        }

        Assert.Equal(expectedCount, deleted);

        var after = await attachments.PreviewCleanupAsync(criteria);
        Assert.Equal(0, after.Data.Count);
    }

    private sealed class FolderDownloadService(string folder, HttpClient http) : StreamedDownloadService(http)
    {
        protected override Task<DownloadDestination> OpenDestinationAsync(
            string fileName, string mimeType, CancellationToken cancellationToken) =>
            Task.FromResult<DownloadDestination>(new FolderDownloadDestination(folder, fileName));
    }

    [Fact]
    public async Task AppDownloadSavesTheArchiveAndTheServerSeesItFinish()
    {
        var attachments = _fixture.Client.AttachmentService;
        var marker = Guid.NewGuid().ToString("N")[..8];
        await UploadTextAsync($"native-{marker}-1.txt", 5000);
        await UploadTextAsync($"native-{marker}-2.txt", 5000);

        var criteria = new AttachmentCleanupCriteria
        {
            AsOf = DateTime.UtcNow.AddMinutes(1),
            Scope = AttachmentCleanupScope.All,
            Categories = [ContentCategory.File],
        };
        var ticket = await attachments.StartCleanupArchiveAsync(criteria);
        Assert.True(ticket.Success, ticket.Message);

        var folder = Path.Combine(Path.GetTempPath(), "valour-archive-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Native hosts download with their own HTTP client, like this one.
            using var http = _fixture.Factory.CreateClient();
            var downloads = new FolderDownloadService(folder, http);
            Assert.False(downloads.UsesBrowserDownloads);

            var result = await downloads.DownloadAsync(new DownloadRequest(
                new Uri(http.BaseAddress!, ticket.Data.DownloadPath).ToString(), "valour-attachments.zip", "application/zip"));

            Assert.True(result.Success, result.Message);
            using (var zip = ZipFile.OpenRead(result.Location!))
            {
                Assert.Contains(zip.Entries, e => e.FullName == "manifest.json");
                Assert.True(zip.Entries.Count(e => e.FullName != "manifest.json") >= 2);
            }

            // The app finishing the download is what lets the delete step proceed.
            var status = await attachments.GetCleanupArchiveStatusAsync(ticket.Data.Id);
            Assert.Equal(AttachmentArchiveState.Completed, status.Data.State);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        // Leave the account as it was found.
        while ((await attachments.DeleteCleanupBatchAsync(new AttachmentCleanupDeleteRequest
               { Criteria = criteria, ArchiveId = ticket.Data.Id })).Data is { Remaining: > 0, Deleted: > 0 })
        {
        }
    }
}
