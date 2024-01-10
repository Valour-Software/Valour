using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using Valour.Server.Cdn.Extensions;
using Valour.Server.Cdn.Objects;
using Valour.Shared.Authorization;
using Valour.Shared.Cdn;

namespace Valour.Server.Cdn.Api;

public class UploadApi
{
    private static readonly HashSet<ExifTag> AllowedExif = new HashSet<ExifTag>()
    {
        ExifTag.ImageWidth,
        ExifTag.ImageDescription,
        ExifTag.ImageLength,

        ExifTag.Orientation,
        ExifTag.DateTime
    };

    public static void AddRoutes(WebApplication app)
    {
        app.MapPost("/upload/profile", ProfileImageRoute);
        app.MapPost("/upload/profilebg", ProfileBackgroundImageRoute);
        app.MapPost("/upload/image", ImageRouteNonPlus);
        app.MapPost("/upload/image/plus", ImageRoutePlus);
        app.MapPost("/upload/planet/{planetId}", PlanetImageRoute);
        app.MapPost("/upload/app/{appId}", AppImageRoute);
        app.MapPost("/upload/file", FileRouteNonPlus);
        app.MapPost("/upload/file/plus", FileRoutePlus);
    }

    public static void HandleExif(Image image)
    {
        // Remove unneeded exif data
        if (image.Metadata != null && image.Metadata.ExifProfile != null)
        {
            List<ExifTag> toRemove = new List<ExifTag>();

            var exifs = image.Metadata.ExifProfile.Values;

            foreach (var exif in exifs)
            {
                if (!AllowedExif.Contains(exif.Tag))
                    toRemove.Add(exif.Tag);
            }

            foreach (var tag in toRemove)
                image.Metadata.ExifProfile.RemoveValue(tag);
        }
    }

    [FileUploadOperation.FileContentType]
    [RequestSizeLimit(20480000)]
    private static async Task<IResult> ImageRoutePlus(HttpContext ctx, ValourDB valourDb, CdnDb db, TokenService tokenService, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        var isPlus = await valourDb.UserSubscriptions.AnyAsync(x => x.UserId == authToken.UserId && x.Active);
        if (!isPlus)
            return ValourResult.Forbid("You must be a Valour Plus subscriber to upload images larger than 10MB");
        
        return await ImageRoute(ctx, valourDb, db, authToken, authorization);
    }

    [FileUploadOperation.FileContentType]
    [RequestSizeLimit(10240000)]
    private static async Task<IResult> ImageRouteNonPlus(HttpContext ctx, ValourDB valourDb, CdnDb db, TokenService tokenService, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        return await ImageRoute(ctx, valourDb, db, authToken, authorization);
    }

    [FileUploadOperation.FileContentType]
    [RequestSizeLimit(10240000)]
    private static async Task<IResult> ImageRoute(HttpContext ctx, ValourDB valourDb, CdnDb db, Models.AuthToken authToken, string authorization)
    {
        if (authToken is null) return ValourResult.InvalidToken();

        var file = ctx.Request.Form.Files.FirstOrDefault();
        if (file is null)
            return Results.BadRequest("Please attach a file");

        if (!CdnUtils.ImageSharpSupported.Contains(file.ContentType))
            return Results.BadRequest("Unsupported file type");

        var imageData = await ProcessImage(file, -1, -1);
        if (imageData is null)
            return Results.BadRequest("Unable to process image. Check format and size.");

        using MemoryStream ms = imageData.Value.stream;
        var bucketResult = await BucketManager.Upload(ms, file.FileName, imageData.Value.extension, authToken.UserId, imageData.Value.mime, ContentCategory.Image, db);

        if (bucketResult.Success)
        {
            return ValourResult.Ok(bucketResult.Message);
        }
        else
        {
            return ValourResult.Problem("There was an issue uploading your image. Try a different format or size.");
        }
    }

    [FileUploadOperation.FileContentType]
    [RequestSizeLimit(10240000)]
    private static async Task<IResult> ProfileImageRoute(HttpContext ctx, ValourDB valourDb, CdnDb db, CoreHubService hubService, TokenService tokenService, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null) return ValourResult.InvalidToken();

        var file = ctx.Request.Form.Files.FirstOrDefault();
        if (file is null)
            return Results.BadRequest("Please attach a file");

        if (!CdnUtils.ImageSharpSupported.Contains(file.ContentType))
            return Results.BadRequest("Unsupported file type");

        var imageData = await ProcessImage(file, 256, 256);
        if (imageData is null)
            return Results.BadRequest("Unable to process image. Check format and size.");

        using MemoryStream ms = imageData.Value.stream;
        var bucketResult = await BucketManager.Upload(ms, file.FileName, imageData.Value.extension, authToken.UserId, imageData.Value.mime, ContentCategory.Profile, db);

        if (bucketResult.Success)
        {
            var user = new Valour.Database.User() { Id = authToken.UserId, PfpUrl = bucketResult.Message };
            valourDb.Users.Attach(user);
            valourDb.Entry(user).Property(x => x.PfpUrl).IsModified = true;
            await valourDb.SaveChangesAsync();

            await hubService.NotifyUserChange(user.ToModel());

            return ValourResult.Ok(bucketResult.Message);
        }
        else
        {
            return ValourResult.Problem("There was an issue uploading your image. Try a different format or size.");
        }
    }
    
    [FileUploadOperation.FileContentType]
    [RequestSizeLimit(10240000)]
    private static async Task<IResult> ProfileBackgroundImageRoute(HttpContext ctx, ValourDB valourDb, CdnDb db, TokenService tokenService, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null) return ValourResult.InvalidToken();
        
        var isPlus = await valourDb.UserSubscriptions.AnyAsync(x => x.UserId == authToken.UserId && x.Active);
        if (!isPlus)
            return ValourResult.Forbid("You must be a Valour Plus subscriber to upload profile backgrounds!");

        var file = ctx.Request.Form.Files.FirstOrDefault();
        if (file is null)
            return Results.BadRequest("Please attach a file");

        if (!CdnUtils.ImageSharpSupported.Contains(file.ContentType))
            return Results.BadRequest("Unsupported file type");

        var imageData = await ProcessImage(file, 300, 400);
        if (imageData is null)
            return Results.BadRequest("Unable to process image. Check format and size.");

        using MemoryStream ms = imageData.Value.stream;

        try
        {
            var bucketResult = await BucketManager.Upload(ms, file.FileName, imageData.Value.extension,
                authToken.UserId, imageData.Value.mime, ContentCategory.Profile, db);

            if (bucketResult.Success)
            {
                var userProfile = new Valour.Database.UserProfile()
                    { Id = authToken.UserId, BackgroundImage = bucketResult.Message };
                valourDb.UserProfiles.Attach(userProfile);
                valourDb.Entry(userProfile).Property(x => x.BackgroundImage).IsModified = true;
                await valourDb.SaveChangesAsync();

                return ValourResult.Ok(bucketResult.Message);
            }
        }
        catch (Exception e) { Console.WriteLine(e.StackTrace); }
        return ValourResult.Problem("There was an issue uploading your image. Try a different format or size.");
    }

    [FileUploadOperation.FileContentType]
    [RequestSizeLimit(10240000)]
    private static async Task<IResult> PlanetImageRoute(
        HttpContext ctx, 
        ValourDB valourDb, 
        CdnDb db, 
        CoreHubService hubService, 
        TokenService tokenService, 
        PlanetService planetService,
        PlanetMemberService memberService, 
        long planetId, 
        [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null) return ValourResult.InvalidToken();

        var member = await memberService.GetByUserAsync(authToken.UserId, planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var file = ctx.Request.Form.Files.FirstOrDefault();
        if (file is null)
            return Results.BadRequest("Please attach a file");

        if (!CdnUtils.ImageSharpSupported.Contains(file.ContentType))
            return Results.BadRequest("Unsupported file type");

        var imageData = await ProcessImage(file, 512, 512);
        if (imageData is null)
            return Results.BadRequest("Unable to process image. Check format and size.");

        using MemoryStream ms = imageData.Value.stream;
        var bucketResult = await BucketManager.Upload(ms, file.FileName, imageData.Value.extension, authToken.UserId, imageData.Value.mime, ContentCategory.Planet, db);

        if (bucketResult.Success)
        {
            var planet = await valourDb.Planets.FindAsync(member.PlanetId);
            planet.IconUrl = bucketResult.Message;
            await valourDb.SaveChangesAsync();

            hubService.NotifyPlanetChange(planet.ToModel());

            return ValourResult.Ok(bucketResult.Message);
        }
        else
        {
            return ValourResult.Problem("There was an issue uploading your image. Try a different format or size.");
        }
    }

    [FileUploadOperation.FileContentType]
    [RequestSizeLimit(10240000)]
    private static async Task<IResult> AppImageRoute(HttpContext ctx, ValourDB valourDb, CdnDb db, TokenService tokenService, long appId, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null) return ValourResult.InvalidToken();

        var app = await valourDb.OauthApps.FindAsync(appId);
        if (app is null)
            return ValourResult.NotFound("Could not find app");

        if (app.OwnerId != authToken.UserId)
            return Results.Unauthorized();

        var file = ctx.Request.Form.Files.FirstOrDefault();
        if (file is null)
            return Results.BadRequest("Please attach a file");

        if (!CdnUtils.ImageSharpSupported.Contains(file.ContentType))
            return Results.BadRequest("Unsupported file type");

        var imageData = await ProcessImage(file, 512, 512);
        if (imageData is null)
            return Results.BadRequest("Unable to process image. Check format and size.");

        using MemoryStream ms = imageData.Value.stream;
        var bucketResult = await BucketManager.Upload(ms, file.FileName, imageData.Value.extension, authToken.UserId, imageData.Value.mime, ContentCategory.App, db);

        if (bucketResult.Success)
        {
            app.ImageUrl = bucketResult.Message;
            await valourDb.SaveChangesAsync();

            return ValourResult.Ok(bucketResult.Message);
        }
        else
        {
            return ValourResult.Problem("There was an issue uploading your image. Try a different format or size.");
        }
    }

    [FileUploadOperation.FileContentType]
    [RequestSizeLimit(20480000)]
    private static async Task<IResult> FileRoutePlus(HttpContext ctx, ValourDB valourDb, CdnDb db, TokenService tokenService, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        var isPlus = await valourDb.UserSubscriptions.AnyAsync(x => x.UserId == authToken.UserId && x.Active);
        if (!isPlus)
            return ValourResult.Forbid("You must be a Valour Plus subscriber to upload files larger than 10MB");
        
        return await FileRoute(ctx, valourDb, db, authToken, authorization);
    }
    
    [FileUploadOperation.FileContentType]
    [RequestSizeLimit(10240000)]
    private static async Task<IResult> FileRouteNonPlus(HttpContext ctx, ValourDB valourDb, CdnDb db, TokenService tokenService, [FromHeader] string authorization)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        return await FileRoute(ctx, valourDb, db, authToken, authorization);
    }
    
    private static async Task<IResult> FileRoute(HttpContext ctx, ValourDB valourDb, CdnDb db, Models.AuthToken authToken, string authorization)
    {
        if (authToken is null) return ValourResult.InvalidToken();

        var file = ctx.Request.Form.Files.FirstOrDefault();
        if (file is null)
            return Results.BadRequest("Please attach a file");

        if (CdnUtils.ImageSharpSupported.Contains(file.ContentType))
            return Results.BadRequest("Unsupported file type");

        string ext = Path.GetExtension(file.FileName);
        using MemoryStream ms = new();
        await file.CopyToAsync(ms);

        var bucketResult = await BucketManager.Upload(ms, file.FileName, ext, authToken.UserId, file.ContentType, ContentCategory.File, db);

        if (bucketResult.Success)
        {
            return ValourResult.Ok(bucketResult.Message);
        }
        else
        {
            return ValourResult.Problem("There was an issue uploading your image. Try a different format or size.");
        }
    }

    private static async Task<(MemoryStream stream, string mime, string extension)?> ProcessImage(IFormFile file, int sizeX, int sizeY)
    {
        var stream = file.OpenReadStream();

        Image image;
        
        if (sizeX + sizeY > 1)
        {
            image = await Image.LoadAsync(
                new() { TargetSize = new(sizeX, sizeY) }, 
                stream
            );
        }
        else
        {
            image = await Image.LoadAsync(stream);
        }

        HandleExif(image);

        // Save image to stream
        MemoryStream ms = new();

        string contentType = image.Metadata.DecodedImageFormat.DefaultMimeType;
        string extension = image.Metadata.DecodedImageFormat.FileExtensions.FirstOrDefault();
        await image.SaveAsync(ms, image.Metadata.DecodedImageFormat);
        
        return (ms, contentType, extension);
    }
}

