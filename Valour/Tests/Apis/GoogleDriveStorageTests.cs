using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Valour.Database;
using Valour.Database.Context;
using Valour.Server.Cdn.Storage;
using Valour.Server.Services;
using Valour.Server.Services.GoogleDrive;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

/// <summary>An in-memory Google Drive, with switches for the failures real Drives have.</summary>
public sealed class FakeGoogleDriveApi : IGoogleDriveApi
{
    public record FakeFolder(string Id, string Name, string ParentId, bool Trashed);

    public record FakeFile(string Id, string Name, string ParentId, string MimeType, byte[] Content, IReadOnlyDictionary<string, string> Properties);

    public Dictionary<string, FakeFolder> Folders { get; } = new();
    public Dictionary<string, FakeFile> Files { get; } = new();

    public bool RevokeAccess { get; set; }
    public bool DriveFull { get; set; }
    public int RefreshCalls { get; private set; }
    public List<string> RevokedTokens { get; } = new();

    private int _nextId = 1;

    public Task<DriveAccessToken> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        RefreshCalls++;
        if (RevokeAccess)
            throw new DriveApiException(DriveErrorKind.AccessRevoked, "revoked");

        return Task.FromResult(new DriveAccessToken("access-" + refreshToken, DateTime.UtcNow.AddMinutes(50)));
    }

    public Task<string> CreateFolderAsync(string accessToken, string name, string parentId, CancellationToken cancellationToken = default)
    {
        var id = "folder-" + _nextId++;
        Folders[id] = new FakeFolder(id, name, parentId, false);
        return Task.FromResult(id);
    }

    public Task<bool> ExistsAsync(string accessToken, string folderId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.ContainsKey(folderId) || (Folders.TryGetValue(folderId, out var folder) && !folder.Trashed));

    public Task RenameAsync(string accessToken, string fileId, string name, CancellationToken cancellationToken = default)
    {
        Folders[fileId] = Folders[fileId] with { Name = name };
        return Task.CompletedTask;
    }

    public async Task<string> UploadFileAsync(string accessToken, string parentId, string name, string mimeType, Stream content, long length,
        IReadOnlyDictionary<string, string> properties, CancellationToken cancellationToken = default)
    {
        if (DriveFull)
            throw new DriveApiException(DriveErrorKind.DriveFull, "full");

        // Real Drive refuses a parent that is gone.
        if (!Folders.TryGetValue(parentId, out var parent) || parent.Trashed)
            throw new DriveApiException(DriveErrorKind.NotFound, "parent missing");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Assert.Equal(length, buffer.Length);

        var id = "file-" + _nextId++;
        Files[id] = new FakeFile(id, name, parentId, mimeType, buffer.ToArray(), properties);
        return id;
    }

    public Task<DriveDownload> OpenDownloadAsync(string accessToken, string fileId, CancellationToken cancellationToken = default)
    {
        if (!Files.TryGetValue(fileId, out var file))
            throw new DriveApiException(DriveErrorKind.NotFound, "gone");

        return Task.FromResult(new DriveDownload(new MemoryStream(file.Content), file.Content.Length));
    }

    public Task RevokeAsync(string token)
    {
        RevokedTokens.Add(token);
        return Task.CompletedTask;
    }

    /// <summary>The folder names from the top of Drive down to a file.</summary>
    public string PathOf(FakeFile file)
    {
        var parts = new List<string>();
        var parentId = file.ParentId;
        while (parentId is not null)
        {
            var folder = Folders[parentId];
            parts.Insert(0, folder.Name);
            parentId = folder.ParentId;
        }

        return string.Join("/", parts);
    }
}

/// <summary>
/// End-to-end tests for copying attachments to Google Drive, expiring Valour's
/// copy, and reading files back, against the real server with an in-memory Drive.
/// </summary>
[Collection("ApiCollection")]
public class GoogleDriveStorageTests
{
    private readonly LoginTestFixture _fixture;

    public GoogleDriveStorageTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class Harness : IDisposable
    {
        public IServiceScope Scope { get; }
        public ValourDb Db { get; }
        public FakeGoogleDriveApi Drive { get; } = new();
        public UserDriveService Drives { get; }
        public DriveSyncService Sync { get; }
        public CdnStorageProvider Storage { get; }
        public IDataProtector Protector { get; }
        public long UserId { get; }

        public Harness(LoginTestFixture fixture)
        {
            UserId = fixture.Client.Me.Id;
            Scope = fixture.Factory.Services.CreateScope();
            var services = Scope.ServiceProvider;
            Db = services.GetRequiredService<ValourDb>();
            Storage = services.GetRequiredService<CdnStorageProvider>();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var protection = services.GetRequiredService<IDataProtectionProvider>();
            Protector = protection.CreateProtector("Valour.GoogleDrive.RefreshToken");

            Drives = new UserDriveService(Db, Drive, protection, cache, NullLogger<UserDriveService>.Instance);
            Sync = new DriveSyncService(Db, Drives, Drive, Storage, cache, NullLogger<DriveSyncService>.Instance);
        }

        public async Task ConnectAsync(string account = "google-account-1")
        {
            await Db.UserDriveConnections.Where(x => x.UserId == UserId).ExecuteDeleteAsync();
            await Db.UserDriveFolders.Where(x => x.UserId == UserId).ExecuteDeleteAsync();
            Db.ChangeTracker.Clear();

            Db.UserDriveConnections.Add(new UserDriveConnection
            {
                UserId = UserId,
                GoogleAccountId = account,
                Email = "person@example.com",
                RefreshTokenEncrypted = Protector.Protect("refresh-" + account),
                Status = DriveConnectionStatus.Active,
                ConnectedAt = DateTime.UtcNow.AddHours(-2),
                AutoSyncFrom = DateTime.UtcNow.AddHours(-2),
            });
            await Db.SaveChangesAsync();
        }

        public async Task CleanUpAsync()
        {
            // The files these tests upload would otherwise be picked up by the next test.
            await Db.CdnBucketItems.Where(x => x.UserId == UserId && x.FileName.StartsWith("drive-")).ExecuteDeleteAsync();
            await Db.UserDriveConnections.Where(x => x.UserId == UserId).ExecuteDeleteAsync();
            await Db.UserDriveFolders.Where(x => x.UserId == UserId).ExecuteDeleteAsync();
        }

        /// <summary>Makes a new upload old enough that the worker copies it.</summary>
        public Task AgeAsync(string id) =>
            Db.CdnBucketItems.Where(x => x.Id == id)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-30)));

        public async Task<CdnBucketItem> ItemAsync(string id)
        {
            Db.ChangeTracker.Clear();
            return await Db.CdnBucketItems.AsNoTracking().FirstAsync(x => x.Id == id);
        }

        public void Dispose() => Scope.Dispose();
    }

    private async Task<(string Id, byte[] Bytes)> UploadTextAsync(string name, int length)
    {
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

        // The response is the file's address, ending in category/user/hash.
        var url = await response.Content.ReadAsStringAsync();
        var parts = url.Split('/');
        return ($"{parts[^3]}/{parts[^2]}/{parts[^1]}", bytes);
    }

    [Fact]
    public async Task NewUploadsGoToDriveThenExpireFromValourAndAreReadBackWhenOpened()
    {
        using var h = new Harness(_fixture);
        await h.ConnectAsync();

        try
        {
            var (id, bytes) = await UploadTextAsync("drive-flow.txt", 3000);
            await h.AgeAsync(id);

            var usedBefore = await UserStorageService.GetUsedBytesAsync(h.Db, h.UserId);

            Assert.True(await h.Sync.SyncBatchAsync() >= 1);

            var synced = await h.ItemAsync(id);
            Assert.Equal(DriveSyncState.Synced, synced.DriveState);
            Assert.NotNull(synced.DriveFileId);
            Assert.NotNull(synced.StorageExpiresAt);
            Assert.True(synced.StorageExpiresAt > DateTime.UtcNow.AddDays(29));

            // Not used in a message, so it goes in Other.
            var file = h.Drive.Files[synced.DriveFileId];
            Assert.Equal("Valour/Other", h.Drive.PathOf(file));
            Assert.Equal(bytes, file.Content);
            Assert.Equal(synced.Sha256Hash, file.Properties["valourSha256"]);

            // Nothing is removed before the copy expires.
            Assert.Equal(0, await h.Sync.PurgeExpiredAsync());

            await h.Db.CdnBucketItems.Where(x => x.Id == id)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.StorageExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            Assert.True(await h.Sync.PurgeExpiredAsync() >= 1);

            Assert.NotNull((await h.ItemAsync(id)).StoragePurgedAt);
            Assert.Null(await h.Storage.Private.GetAsync(synced.Hash));
            Assert.True(await UserStorageService.GetUsedBytesAsync(h.Db, h.UserId) <= usedBefore - 3000);

            // Opening the file reads it back from Drive. The test host has no Drive
            // configured, so the content route is exercised through the same service.
            var restored = await h.Sync.RestoreAsync(await h.ItemAsync(id));
            Assert.True(restored);
            Assert.Null((await h.ItemAsync(id)).StoragePurgedAt);
            await using var back = await h.Storage.Private.GetAsync(synced.Hash);
            Assert.NotNull(back);
            using var read = new MemoryStream();
            await back!.Stream.CopyToAsync(read);
            Assert.Equal(bytes, read.ToArray());
        }
        finally
        {
            await h.CleanUpAsync();
        }
    }

    [Fact]
    public async Task RevokedAccessKeepsValourCopiesAndAsksToReconnect()
    {
        using var h = new Harness(_fixture);
        await h.ConnectAsync();

        try
        {
            var (id, _) = await UploadTextAsync("drive-revoked.txt", 2000);
            await h.AgeAsync(id);

            h.Drive.RevokeAccess = true;
            Assert.Equal(0, await h.Sync.SyncBatchAsync());

            var connection = await h.Db.UserDriveConnections.AsNoTracking().FirstAsync(x => x.UserId == h.UserId);
            Assert.Equal(DriveConnectionStatus.NeedsReconnect, connection.Status);
            Assert.NotNull(connection.LastError);

            // The file waits, and the account is limited by its plan again.
            Assert.Equal(DriveSyncState.Pending, (await h.ItemAsync(id)).DriveState);
            Assert.False(await UserDriveService.HasActiveConnectionAsync(h.Db, h.UserId));
            Assert.Equal(UserSubscriptionTypes.DefaultStorageQuotaBytes, await UserStorageService.GetQuotaBytesAsync(h.Db, h.UserId));
        }
        finally
        {
            await h.CleanUpAsync();
        }
    }

    [Fact]
    public async Task FullDriveHoldsFilesBackWithoutCountingFailures()
    {
        using var h = new Harness(_fixture);
        await h.ConnectAsync();

        try
        {
            var (id, _) = await UploadTextAsync("drive-full.txt", 2000);
            await h.AgeAsync(id);

            h.Drive.DriveFull = true;
            Assert.Equal(0, await h.Sync.SyncBatchAsync());

            var item = await h.ItemAsync(id);
            Assert.Equal(DriveSyncState.Pending, item.DriveState);
            Assert.Equal(0, item.DriveAttempts);
            Assert.True(item.DriveNextAttemptAt > DateTime.UtcNow);

            var info = await h.Drives.GetInfoAsync(h.UserId);
            Assert.Contains("full", info.LastError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await h.CleanUpAsync();
        }
    }

    [Fact]
    public async Task AFolderDeletedInDriveIsMadeAgain()
    {
        using var h = new Harness(_fixture);
        await h.ConnectAsync();

        try
        {
            var (first, _) = await UploadTextAsync("drive-folder-1.txt", 2000);
            await h.AgeAsync(first);
            Assert.True(await h.Sync.SyncBatchAsync() >= 1);

            // The person deletes the whole Valour folder from their Drive.
            h.Drive.Folders.Clear();
            h.Drive.Files.Clear();

            var (second, _) = await UploadTextAsync("drive-folder-2.txt", 2000);
            await h.AgeAsync(second);
            Assert.True(await h.Sync.SyncBatchAsync() >= 1);

            var item = await h.ItemAsync(second);
            Assert.Equal(DriveSyncState.Synced, item.DriveState);
            Assert.Equal("Valour/Other", h.Drive.PathOf(h.Drive.Files[item.DriveFileId]));
        }
        finally
        {
            await h.CleanUpAsync();
        }
    }

    [Fact]
    public async Task DisconnectingKeepsFilesThatLiveOnlyInDrive()
    {
        using var h = new Harness(_fixture);
        await h.ConnectAsync();

        try
        {
            var (kept, _) = await UploadTextAsync("drive-kept.txt", 2000);
            var (cached, _) = await UploadTextAsync("drive-cached.txt", 2000);
            await h.AgeAsync(kept);
            await h.AgeAsync(cached);
            await h.Sync.SyncBatchAsync();

            await h.Db.CdnBucketItems.Where(x => x.Id == kept)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.StorageExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            await h.Sync.PurgeExpiredAsync();

            Assert.True((await h.Drives.DisconnectAsync(h.UserId)).Success);
            Assert.Single(h.Drive.RevokedTokens);

            // The file still on Valour is an ordinary file again.
            var stillHere = await h.ItemAsync(cached);
            Assert.Equal(DriveSyncState.None, stillHere.DriveState);
            Assert.Null(stillHere.DriveFileId);
            Assert.Null(stillHere.StorageExpiresAt);

            // The file only in Drive keeps its Drive record, so reconnecting the same
            // account makes it readable again.
            var driveOnly = await h.ItemAsync(kept);
            Assert.NotNull(driveOnly.StoragePurgedAt);
            Assert.NotNull(driveOnly.DriveFileId);

            // With no connection it can't be read back.
            Assert.False(await h.Sync.RestoreAsync(driveOnly));
        }
        finally
        {
            await h.CleanUpAsync();
        }
    }

    [Fact]
    public async Task KeepingNewUploadsOnValourStopsAutomaticCopyingAndRestoresTheLimits()
    {
        using var h = new Harness(_fixture);
        await h.ConnectAsync();

        try
        {
            Assert.True((await h.Drives.SetNewUploadsToDriveAsync(h.UserId, false)).Success);

            var (id, _) = await UploadTextAsync("drive-valour-default.txt", 2000);
            await h.AgeAsync(id);
            Assert.Equal(0, await h.Sync.SyncBatchAsync());
            Assert.Equal(DriveSyncState.None, (await h.ItemAsync(id)).DriveState);

            // Drive is connected but is no longer the default, so plan limits apply again.
            Assert.False(await UserDriveService.IsDriveDefaultAsync(h.Db, h.UserId));
            Assert.Equal(UserSubscriptionTypes.DefaultMaxUploadBytes, await UserStorageService.GetMaxUploadBytesAsync(h.Db, h.UserId));
            Assert.Equal(UserSubscriptionTypes.DefaultStorageQuotaBytes, await UserStorageService.GetQuotaBytesAsync(h.Db, h.UserId));
            var info = await UserStorageService.GetInfoAsync(h.Db, h.UserId);
            Assert.True(info.DriveConnected);
            Assert.False(info.DriveIsDefault);

            // Switching back only affects files uploaded from then on.
            Assert.True((await h.Drives.SetNewUploadsToDriveAsync(h.UserId, true)).Success);
            Assert.Equal(0, await h.Sync.SyncBatchAsync());
            Assert.Equal(DriveSyncState.None, (await h.ItemAsync(id)).DriveState);

            var (later, _) = await UploadTextAsync("drive-after-switch.txt", 2000);
            await h.AgeAsync(later);
            await h.Db.UserDriveConnections.Where(x => x.UserId == h.UserId)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.AutoSyncFrom, DateTime.UtcNow.AddMinutes(-40)));
            await h.Db.CdnBucketItems.Where(x => x.Id == id)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-50)));
            Assert.True(await h.Sync.SyncBatchAsync() >= 1);
            Assert.Equal(DriveSyncState.Synced, (await h.ItemAsync(later)).DriveState);
            Assert.Equal(DriveSyncState.None, (await h.ItemAsync(id)).DriveState);
        }
        finally
        {
            await h.CleanUpAsync();
        }
    }

    [Fact]
    public async Task AFileCanBeSentToDriveAndMovedBackAndForth()
    {
        using var h = new Harness(_fixture);
        await h.ConnectAsync();

        try
        {
            await h.Drives.SetNewUploadsToDriveAsync(h.UserId, false);
            var (id, bytes) = await UploadTextAsync("drive-roundtrip.txt", 2500);
            var parts = id.Split('/');
            var category = Enum.Parse<ContentCategory>(parts[0]);
            var hash = parts[2];

            // Valour to Drive: queued, copied, and Valour's copy goes right away.
            var queued = await h.Sync.SendToDriveAsync(h.UserId, category, hash);
            Assert.True(queued.Success, queued.Message);
            Assert.Equal(AttachmentStorageLocation.SendingToDrive, queued.Data.StorageLocation);

            await h.Sync.RunPassAsync();
            var sent = await h.ItemAsync(id);
            Assert.Equal(DriveSyncState.Synced, sent.DriveState);
            Assert.NotNull(sent.StoragePurgedAt);
            Assert.Null(await h.Storage.Private.GetAsync(sent.Hash));
            Assert.Single(h.Drive.Files);

            // Drive to Valour: read back, kept, and never expired or copied again.
            var back = await h.Sync.MoveToValourAsync(h.UserId, category, hash);
            Assert.True(back.Success, back.Message);
            Assert.Equal(AttachmentStorageLocation.Valour, back.Data.StorageLocation);

            var pinned = await h.ItemAsync(id);
            Assert.Equal(DriveSyncState.Pinned, pinned.DriveState);
            Assert.Null(pinned.StoragePurgedAt);
            Assert.Null(pinned.StorageExpiresAt);
            await using (var download = await h.Storage.Private.GetAsync(pinned.Hash))
            {
                Assert.NotNull(download);
                using var read = new MemoryStream();
                await download!.Stream.CopyToAsync(read);
                Assert.Equal(bytes, read.ToArray());
            }

            await h.Sync.RunPassAsync();
            Assert.Equal(DriveSyncState.Pinned, (await h.ItemAsync(id)).DriveState);

            // Valour to Drive again reuses the copy that is still in Drive.
            var again = await h.Sync.SendToDriveAsync(h.UserId, category, hash);
            Assert.True(again.Success, again.Message);
            Assert.Equal(AttachmentStorageLocation.DriveWithValourCopy, again.Data.StorageLocation);
            await h.Sync.RunPassAsync();
            h.Db.ChangeTracker.Clear();
            Assert.Equal(AttachmentStorageLocation.DriveOnly, (await h.Sync.SendToDriveAsync(h.UserId, category, hash)).Data.StorageLocation);
            Assert.Single(h.Drive.Files);

            // A file that is not in Drive can't be moved back.
            var (plain, _) = await UploadTextAsync("drive-plain.txt", 1500);
            var plainParts = plain.Split('/');
            var refused = await h.Sync.MoveToValourAsync(h.UserId, Enum.Parse<ContentCategory>(plainParts[0]), plainParts[2]);
            Assert.False(refused.Success);
        }
        finally
        {
            await h.CleanUpAsync();
        }
    }

    [Fact]
    public async Task MovingBackToValourNeedsRoomInThePlansAllowance()
    {
        using var h = new Harness(_fixture);
        await h.ConnectAsync();

        try
        {
            var (id, _) = await UploadTextAsync("drive-no-room.txt", 2000);
            var parts = id.Split('/');
            var category = Enum.Parse<ContentCategory>(parts[0]);

            await h.Sync.SendToDriveAsync(h.UserId, category, parts[2]);
            await h.Sync.RunPassAsync();
            Assert.NotNull((await h.ItemAsync(id)).StoragePurgedAt);

            var fillerId = $"File/{h.UserId}/drive-filler-{Guid.NewGuid():N}";
            h.Db.CdnBucketItems.Add(new CdnBucketItem
            {
                Id = fillerId,
                Hash = fillerId.Split('/')[^1],
                UserId = h.UserId,
                MimeType = "application/octet-stream",
                FileName = "drive-filler.bin",
                Category = ContentCategory.File,
                SizeBytes = int.MaxValue,
                CreatedAt = DateTime.UtcNow,
                Sha256Hash = new string('0', 64),
            });
            h.Db.CdnBucketItems.Add(new CdnBucketItem
            {
                Id = fillerId + "-b",
                Hash = fillerId.Split('/')[^1] + "-b",
                UserId = h.UserId,
                MimeType = "application/octet-stream",
                FileName = "drive-filler.bin",
                Category = ContentCategory.File,
                SizeBytes = int.MaxValue,
                CreatedAt = DateTime.UtcNow,
                Sha256Hash = new string('0', 64),
            });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
            // Two 2 GiB records fit a 5 GiB plan, so a third fills it.
            h.Db.CdnBucketItems.Add(new CdnBucketItem
            {
                Id = fillerId + "-c",
                Hash = fillerId.Split('/')[^1] + "-c",
                UserId = h.UserId,
                MimeType = "application/octet-stream",
                FileName = "drive-filler.bin",
                Category = ContentCategory.File,
                SizeBytes = int.MaxValue,
                CreatedAt = DateTime.UtcNow,
                Sha256Hash = new string('0', 64),
            });
            await h.Db.SaveChangesAsync();

            var refused = await h.Sync.MoveToValourAsync(h.UserId, category, parts[2]);
            Assert.False(refused.Success);
            Assert.Equal(413, refused.Code);
            Assert.NotNull((await h.ItemAsync(id)).StoragePurgedAt);
        }
        finally
        {
            await h.CleanUpAsync();
        }
    }

    [Fact]
    public async Task ADriveRaisesTheFileLimitAndLiftsTheQuota()
    {
        using var h = new Harness(_fixture);

        Assert.Equal(UserSubscriptionTypes.DefaultMaxUploadBytes, await UserStorageService.GetMaxUploadBytesAsync(h.Db, h.UserId));

        await h.ConnectAsync();
        try
        {
            Assert.True(await UserStorageService.GetMaxUploadBytesAsync(h.Db, h.UserId) >= GoogleDriveLimits.ConnectedMaxUploadBytes);
            Assert.Equal(long.MaxValue, await UserStorageService.GetQuotaBytesAsync(h.Db, h.UserId));

            var info = await UserStorageService.GetInfoAsync(h.Db, h.UserId);
            Assert.True(info.DriveConnected);
        }
        finally
        {
            await h.CleanUpAsync();
        }
    }

    [Fact]
    public void SigningInWithGoogleNeverAsksForDriveButConnectingDoes()
    {
        var previous = Valour.Config.Configs.ExternalAuthConfig.Current;
        try
        {
            _ = new Valour.Config.Configs.ExternalAuthConfig
            {
                Google = new Valour.Config.Configs.ExternalAuthProviderConfig { ClientId = "id", ClientSecret = "secret" }
            };

            var provider = new Valour.Server.Services.ExternalAuth.GoogleAuthProvider(
                new FakeHttpClientFactory(), NullLogger<Valour.Server.Services.ExternalAuth.GoogleAuthProvider>.Instance);

            static Dictionary<string, string> Parse(string url) =>
                System.Web.HttpUtility.ParseQueryString(new Uri(url).Query).AllKeys
                    .ToDictionary(k => k!, k => System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)[k]!);

            foreach (var intent in new[] { ExternalAuthIntent.Login, ExternalAuthIntent.Link, ExternalAuthIntent.Reauth })
            {
                var signIn = Parse(provider.BuildAuthorizationUrl("https://example.test/cb", "state", intent));
                Assert.Equal("openid email profile", signIn["scope"]);
                Assert.DoesNotContain("drive", signIn["scope"]);
                Assert.False(signIn.ContainsKey("access_type"));
            }

            var connect = Parse(provider.BuildAuthorizationUrl("https://example.test/cb", "state", ExternalAuthIntent.ConnectDrive));
            Assert.Contains("https://www.googleapis.com/auth/drive.file", connect["scope"].Split(' '));
            Assert.DoesNotContain("https://www.googleapis.com/auth/drive ", connect["scope"] + " ");
            Assert.Equal("offline", connect["access_type"]);
            Assert.Contains("consent", connect["prompt"]);
            Assert.Equal("false", connect["include_granted_scopes"]);
        }
        finally
        {
            Valour.Config.Configs.ExternalAuthConfig.Current = previous;
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Theory]
    [InlineData("  Photos  ", "Photos")]
    [InlineData("a\tb\nc", "abc")]
    [InlineData("", "Untitled")]
    [InlineData(null, "Untitled")]
    public void FolderNamesAreCleanedForDrive(string name, string expected)
    {
        Assert.Equal(expected, DriveSyncService.SanitizeName(name, "Untitled"));
    }

    [Theory]
    [InlineData("root", null)]
    [InlineData("planets", "root")]
    [InlineData("dms", "root")]
    [InlineData("other", "root")]
    [InlineData("planet:42", "planets")]
    [InlineData("planet:42:2026-11", "planet:42")]
    [InlineData("dm:7", "dms")]
    [InlineData("dm:7:2026-11", "dm:7")]
    public void FoldersKnowWhichFolderTheyAreIn(string key, string parent)
    {
        Assert.Equal(parent, DriveSyncService.ParentKey(key));
    }

    [Fact]
    public void LongFolderNamesAreShortened()
    {
        var name = DriveSyncService.SanitizeName(new string('x', 400), "Untitled");
        Assert.Equal(100, name.Length);
    }
}
