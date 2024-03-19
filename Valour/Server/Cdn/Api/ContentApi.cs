using Amazon.S3.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Web.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using Valour.Server.Cdn.Objects;

namespace Valour.Server.Cdn.Api;

public class ContentApi : Controller
{

    public static void AddRoutes(WebApplication app)
    {
        app.MapGet("/content/{category}/{userId}/{hash}", GetRoute);
        app.MapGet("/content/migrateAvatars", MigrateRoute);
        app.MapGet("/content/migratePlanets", MigratePlanetsRoute);
    }
    
    private static async Task<IResult> MigrateRoute(TokenService tokenService, CdnMemoryCache cache, ValourDB db)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        if (token is null)
            return ValourResult.Forbid("Route is for Admins only.");        
        
        var authUser = await db.Users.FindAsync(token.UserId);
        if (authUser is null || !authUser.ValourStaff)
            return ValourResult.Forbid("Route is for Admins only.");

        var users = await db.Users.ToListAsync();
        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.OldAvatarUrl))
                continue;
            
            var bucketId = user.OldAvatarUrl.Replace("https://cdn.valour.gg/content/", "");
            
            var avatarItem = await db.CdnBucketItems.FirstOrDefaultAsync(x =>
                x.Id == bucketId);
            
            if (avatarItem is null)
                continue;
            
            if (avatarItem.Category == Valour.Database.ContentCategory.Profile)
            {
                // Ok so here we pull down the image and re-upload it to the public bucket
                
                var request = new GetObjectRequest()
                {
                    Key = avatarItem.Hash,
                    BucketName = "valourmps",
                };
                
                var response = await BucketManager.Client.GetObjectAsync(request);

                if (!IsSuccessStatusCode(response.HttpStatusCode))
                {
                    Console.WriteLine("Failed to migrate avatar: " + avatarItem.Hash);
                    continue;
                }

                try
                {
                    var image = await Image.LoadAsync(new DecoderOptions()
                    {
                        TargetSize = new Size(UploadApi.AvatarSizes[0].Width, UploadApi.AvatarSizes[0].Height),
                    }, response.ResponseStream);

                    UploadApi.HandleExif(image);

                    var result = await UploadApi.UploadPublicImageVariants(image, "avatars", avatarItem.UserId.ToString(), UploadApi.AvatarSizes,
                        0, true, false);

                    if (result.Success)
                    {
                        user.HasCustomAvatar = true;
                        user.HasAnimatedAvatar = result.Data;
                        await db.SaveChangesAsync();
                        
                        Console.WriteLine("Migrated avatar: " + avatarItem.Hash);
                    }
                    else
                    {
                        Console.WriteLine("Failed to migrate avatar (Final step): " + avatarItem.Hash);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to migrate avatar: " + avatarItem.Hash);
                    Console.WriteLine(e);
                    
                    continue;
                }
            }
        }

        return Results.Ok();
    }
    
    private static async Task<IResult> MigratePlanetsRoute(TokenService tokenService, CdnMemoryCache cache, ValourDB db)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        if (token is null)
            return ValourResult.Forbid("Route is for Admins only.");        
        
        var authUser = await db.Users.FindAsync(token.UserId);
        if (authUser is null || !authUser.ValourStaff)
            return ValourResult.Forbid("Route is for Admins only.");

        var planets = await db.Planets.ToListAsync();
        foreach (var planet in planets)
        {
            if (string.IsNullOrWhiteSpace(planet.OldIconUrl))
                continue;
            
            var bucketId = planet.OldIconUrl.Replace("https://cdn.valour.gg/content/", "");
            
            var iconItem = await db.CdnBucketItems.FirstOrDefaultAsync(x =>
                x.Id == bucketId);
            
            if (iconItem is null)
                continue;
            
            if (iconItem.Category == Valour.Database.ContentCategory.Planet)
            {
                // Ok so here we pull down the image and re-upload it to the public bucket
                
                var request = new GetObjectRequest()
                {
                    Key = iconItem.Hash,
                    BucketName = "valourmps",
                };
                
                var response = await BucketManager.Client.GetObjectAsync(request);

                if (!IsSuccessStatusCode(response.HttpStatusCode))
                {
                    Console.WriteLine("Failed to migrate planet icon: " + iconItem.Hash);
                    continue;
                }

                try
                {
                    var image = await Image.LoadAsync(new DecoderOptions()
                    {
                        TargetSize = new Size(UploadApi.AvatarSizes[0].Width, UploadApi.AvatarSizes[0].Height),
                    }, response.ResponseStream);

                    UploadApi.HandleExif(image);

                    var result = await UploadApi.UploadPublicImageVariants(image, "planets", planet.Id.ToString(), UploadApi.PlanetSizes,
                        0, true, false);

                    if (result.Success)
                    {
                        planet.HasCustomIcon = true;
                        planet.HasAnimatedIcon = result.Data;
                        await db.SaveChangesAsync();
                        
                        Console.WriteLine("Migrated planet icon: " + iconItem.Hash);
                    }
                    else
                    {
                        Console.WriteLine("Failed to migrate planet icon (Final step): " + iconItem.Hash);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to migrate planet icon: " + iconItem.Hash);
                    Console.WriteLine(e);
                    
                    continue;
                }
            }
        }

        return Results.Ok();
    }

    private static async Task<IResult> GetRoute(CdnMemoryCache cache, ValourDB db,
         ContentCategory category, string hash, ulong userId)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return Results.BadRequest("Include id.");

        if (userId == 0)
            return Results.BadRequest("Include user id.");

        var id = $"{category}/{userId}/{hash}";

        var bucketItemRecord = await db.CdnBucketItems.FindAsync(id);
        if (bucketItemRecord is null)
            return Results.NotFound();

        byte[] data;

        if (!cache.Cache.TryGetValue(hash, out data))
        {
            var request = new GetObjectRequest()
            {
                Key = hash,
                BucketName = "valourmps",
            };

            var response = await BucketManager.Client.GetObjectAsync(request);

            if (!IsSuccessStatusCode(response.HttpStatusCode))
                return Results.BadRequest("Failed to get object from bucket.");

            MemoryStream ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms);

            data = ms.ToArray();

            cache.Cache.Set(hash, data, 
                new MemoryCacheEntryOptions() 
                { 
                    Size = data.Length, 
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) 
                }
             );
        }

        return Results.File(data, bucketItemRecord.MimeType, bucketItemRecord.FileName, enableRangeProcessing: true);
    }

    public static bool IsSuccessStatusCode(HttpStatusCode statusCode)
    {
        var intStatus = (int)statusCode;
        return (intStatus >= 200) && (intStatus <= 299);
    }
}
