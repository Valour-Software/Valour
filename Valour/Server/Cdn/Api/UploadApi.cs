using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;
using SixLabors.ImageSharp.Formats;
using Valour.Database;
using Valour.Server.Cdn.Extensions;
using Valour.Server.Cdn.Objects;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared;
using Valour.Shared.Authorization;

namespace Valour.Server.Cdn.Api
{
    public class UploadApi
    {
        static SHA256 SHA256 = SHA256.Create();

        public static JpegEncoder _jpegEncoder = new()
        {
            Quality = 80,
            ColorType = JpegEncodingColor.YCbCrRatio444
        };

        public static PngEncoder _pngEncoder = new()
        {
            CompressionLevel = PngCompressionLevel.BestCompression
        };

        public static GifEncoder _gifEncoder = new()
        {
            
        };

        public static HashSet<ExifTag> AllowedExif = new HashSet<ExifTag>()
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
            app.MapPost("/upload/image", ImageRoute);
            app.MapPost("/upload/planet/{planetId}", PlanetImageRoute);
            app.MapPost("/upload/app/{appId}", AppImageRoute);
            app.MapPost("/upload/file", FileRoute);
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
        [RequestSizeLimit(10240000)]
        private static async Task<IResult> ImageRoute(HttpContext ctx, ValourDB valourDb, CdnDb db, TokenService tokenService, [FromHeader] string authorization)
        {
            var authToken = await tokenService.GetCurrentToken();
            if (authToken is null) return ValourResult.InvalidToken();

            var file = ctx.Request.Form.Files.FirstOrDefault();
            if (file is null)
                return Results.BadRequest("Please attach a file");

            if (!ImageContent.Contains(file.ContentType))
                return Results.BadRequest("Unsupported file type");

            var imageData = await ProcessImage(file);
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
                return ValourResult.Problem(bucketResult.Message);
            }
        }

        [FileUploadOperation.FileContentType]
        [RequestSizeLimit(10240000)]
        private static async Task<IResult> ProfileImageRoute(HttpContext ctx, ValourDB valourDb, CdnDb db, CoreHubService hubService, TokenService tokenService, [FromHeader] string authorization)
        {
            var authToken = await tokenService.GetCurrentToken();
            if (authToken is null) return ValourResult.InvalidToken();

            var file = ctx.Request.Form.Files.FirstOrDefault();
            if (file is null)
                return Results.BadRequest("Please attach a file");

            if (!ImageContent.Contains(file.ContentType))
                return Results.BadRequest("Unsupported file type");

            var imageData = await ProcessImage(file, 256);
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
                return ValourResult.Problem(bucketResult.Message);
            }
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
            var authToken = await tokenService.GetCurrentToken();
            if (authToken is null) return ValourResult.InvalidToken();

            var member = await memberService.GetByUserAsync(authToken.UserId, planetId);
            if (member is null)
                return ValourResult.NotPlanetMember();

            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
                return ValourResult.LacksPermission(PlanetPermissions.Manage);

            var file = ctx.Request.Form.Files.FirstOrDefault();
            if (file is null)
                return Results.BadRequest("Please attach a file");

            if (!ImageContent.Contains(file.ContentType))
                return Results.BadRequest("Unsupported file type");

            var imageData = await ProcessImage(file, 512);
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
                return ValourResult.Problem(bucketResult.Message);
            }
        }

        [FileUploadOperation.FileContentType]
        [RequestSizeLimit(10240000)]
        private static async Task<IResult> AppImageRoute(HttpContext ctx, ValourDB valourDb, CdnDb db, TokenService tokenService, long appId, [FromHeader] string authorization)
        {
            var authToken = await tokenService.GetCurrentToken();
            if (authToken is null) return ValourResult.InvalidToken();

            var app = await valourDb.OauthApps.FindAsync(appId);
            if (app is null)
                return ValourResult.NotFound("Could not find app");

            if (app.OwnerId != authToken.UserId)
                return Results.Unauthorized();

            var file = ctx.Request.Form.Files.FirstOrDefault();
            if (file is null)
                return Results.BadRequest("Please attach a file");

            if (!ImageContent.Contains(file.ContentType))
                return Results.BadRequest("Unsupported file type");

            var imageData = await ProcessImage(file, 512);
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
                return ValourResult.Problem(bucketResult.Message);
            }
        }

        [FileUploadOperation.FileContentType]
        [RequestSizeLimit(10240000)]
        private static async Task<IResult> FileRoute(HttpContext ctx, ValourDB valourDb, CdnDb db, TokenService tokenService, [FromHeader] string authorization)
        {
            var authToken = await tokenService.GetCurrentToken();
            if (authToken is null) return ValourResult.InvalidToken();

            var file = ctx.Request.Form.Files.FirstOrDefault();
            if (file is null)
                return Results.BadRequest("Please attach a file");

            if (ImageContent.Contains(file.ContentType))
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
                return ValourResult.Problem(bucketResult.Message);
            }
        }

        private static async Task<(MemoryStream stream, string mime, string extension)?> ProcessImage(IFormFile file, int size = -1)
        {
            var stream = file.OpenReadStream();

            Image image;
            
            if (size != -1)
            {
                image = await Image.LoadAsync(
                    new() { TargetSize = new(size, size) }, 
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

        ////////////////////
        // Helper methods //
        ////////////////////

        public static HashSet<string> ImageContent = new HashSet<string>()
        {
            "image/gif",
            "image/jpeg",
            "image/png",
            "image/tiff",
            "image/vnd.microsoft.icon",
            "image/x-icon",
            "image/vnd.djvu",
            "image/svg+xml"
        };
    }
}
