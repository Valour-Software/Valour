using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class UserAttachmentServiceTests(LoginTestFixture fixture)
{
    [Fact]
    public async Task DeleteUploadUsedInThread_PreservesMissingAttachmentPlaceholder()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var hash = Guid.NewGuid().ToString("N");
        var id = $"Image/{fixture.Client.Me.Id}/{hash}";
        var threadId = IdManager.Generate();
        var attachmentId = IdManager.Generate();
        db.CdnBucketItems.Add(new Valour.Database.CdnBucketItem
        {
            Id = id, Hash = hash, UserId = fixture.Client.Me.Id, MimeType = "image/webp",
            FileName = "test.webp", Category = ContentCategory.Image, CreatedAt = DateTime.UtcNow, SizeBytes = 10
        });
        db.PlanetThreads.Add(new Valour.Database.PlanetThread
        {
            Id = threadId, PlanetId = ISharedPlanet.ValourCentralId, AuthorUserId = fixture.Client.Me.Id,
            Title = "Attachment regression", Content = "Body", TimeCreated = DateTime.UtcNow
        });
        db.ThreadAttachments.Add(new Valour.Database.ThreadAttachment
        {
            Id = attachmentId, ThreadId = threadId, CdnBucketItemId = id, Location = "https://example.test/test.webp",
            Type = MessageAttachmentType.Image, MimeType = "image/webp", FileName = "test.webp", Width = 10, Height = 10
        });
        await db.SaveChangesAsync();
        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<UserAttachmentService>()
                .DeleteAsync(fixture.Client.Me.Id, ContentCategory.Image, hash);
            Assert.True(result.Success, result.Message);
            Assert.False(await db.CdnBucketItems.AnyAsync(x => x.Id == id));
            var saved = await db.ThreadAttachments.AsNoTracking().SingleAsync(x => x.Id == attachmentId);
            Assert.True(saved.Missing);
            Assert.Null(saved.CdnBucketItemId);
            Assert.Equal(Valour.Sdk.Models.MessageAttachment.MissingLocation, saved.Location);
        }
        finally
        {
            await db.ThreadAttachments.Where(x => x.Id == attachmentId).ExecuteDeleteAsync();
            await db.PlanetThreads.IgnoreQueryFilters().Where(x => x.Id == threadId).ExecuteDeleteAsync();
            await db.CdnBucketItems.Where(x => x.Id == id).ExecuteDeleteAsync();
        }
    }
}
