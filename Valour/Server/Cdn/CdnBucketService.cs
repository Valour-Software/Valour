using Amazon.S3;
using Amazon.S3.Model;
using System.Net;
using System.Security.Cryptography;
using CloudFlare.Client;
using CloudFlare.Client.Api.Zones;
using Valour.Config.Configs;
using Valour.Database;
using Valour.Shared;

namespace Valour.Server.Cdn;

public class CdnBucketService
{
    public static AmazonS3Client Client { get; set; }
    public static AmazonS3Client PublicClient { get; set; }

    private readonly ILogger<CdnBucketService> _logger;
    private readonly ICloudFlareClient _cloudflare;
    private readonly string _zone;
    
    public CdnBucketService(ICloudFlareClient cloudflare, ILogger<CdnBucketService> logger)
    {
        _cloudflare = cloudflare;
        _logger = logger;
        _zone = CloudflareConfig.Instance.ZoneId;
    }

    public async Task<TaskResult> UploadPublicImage(Stream data, string path)
    {
        PutObjectRequest request = new()
        {
            Key = path,
            InputStream = data,
            BucketName = "valour-public",
            DisablePayloadSigning = true
        };
        
        try {
            var response = await PublicClient.PutObjectAsync(request);
            
            if (response.HttpStatusCode == HttpStatusCode.OK)
            {
                if (!string.IsNullOrWhiteSpace(_zone))
                {
                    // Purge the cache for this object
                    var purgeResult =
                        await _cloudflare.Zones.PurgeFilesAsync(_zone, [$"https://public-cdn.valour.gg/{path}"]);

                    if (!purgeResult.Success)
                    {
                        _logger.LogError("Failed to purge cache for {Path}: {Error}", path, purgeResult.Errors);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully purged cache for {Path}", path);
                    }
                }
                
                _logger.LogInformation("Successfully PUT object into bucket: {Path}", path);

                return new TaskResult(true, $"https://public-cdn.valour.gg/{path}");
            }
            else
            {
                _logger.LogError("Failed to PUT object into bucket: {Path} ({StatusCode})", path, response.HttpStatusCode);
                return new TaskResult(false, $"Failed to PUT object into bucket. ({response.HttpStatusCode})");
            }
        }
        catch (Exception e)
        {
            return new TaskResult(false, e.Message);
        }
    }

    public async Task<TaskResult> Upload(MemoryStream data, string fileName, string extension, long userId, string mime, 
        ContentCategory category, ValourDb db)
    {
        // Get hash from image
        var hashBytes = SHA256.HashData(data.GetBuffer());
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

        // Add file extension to the end
        hash += extension;

        // Check if this same file has already been posted by this user.
        var id = $"{category}/{userId}/{hash}";

        // If so, return the location (wooo easy route)
        if (await db.CdnBucketItems.AnyAsync(x => x.Id == id))
            return new TaskResult(true, $"https://cdn.valour.gg/content/{id}");

        // We need a bucket record no matter what at this point
        var bucketRecord = new CdnBucketItem()
        {
            Id = id,
            Category = category,
            Hash = hash,
            MimeType = mime,
            UserId = userId,
            FileName = fileName,
            SizeBytes = (int)data.Length,
            CreatedAt = DateTime.UtcNow
        };

        // Now we check if anyone else has already posted this file.
        // If so, we can just create a new path to the file
        if (await db.CdnBucketItems.AnyAsync(x => x.Hash == hash))
        {
            // Alright, someone else posted this. Let's make a new route to this
            // object without actually re-uploading it.
            try
            {
                await db.CdnBucketItems.AddAsync(bucketRecord);
                await db.SaveChangesAsync();
            }
            catch(System.Exception e)
            {
                Console.WriteLine("Critical error when adding new route to existing bucket item.");
                Console.WriteLine(e.Message);

                return new TaskResult(false, "Critical error when adding new route to existing bucket item."); 
            }

            return new TaskResult(true, $"https://cdn.valour.gg/content/{id}");
        }

        // This object is unique and has to be posted to the bucket
        PutObjectRequest request = new()
        {
            Key = hash,
            // ChecksumAlgorithm = ChecksumAlgorithm.SHA256,
            InputStream = data,
            BucketName = "valourmps",
            DisablePayloadSigning = true
        };

        var response = await Client.PutObjectAsync(request);

        if (!IsSuccessStatusCode(response.HttpStatusCode))
        {
            return new TaskResult(false, $"Failed to PUT object into bucket. ({response.HttpStatusCode})");
        }
        else
        {
            try
            {
                await db.CdnBucketItems.AddAsync(bucketRecord);
                await db.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                Console.WriteLine("Critical error when adding route to new item.");
                Console.WriteLine(e.Message);

                return new TaskResult(false, "Critical error when adding new route to existing bucket item.");
            }

            return new TaskResult(true, $"https://cdn.valour.gg/content/{id}");
        }
    }

    public static bool IsSuccessStatusCode(HttpStatusCode statusCode)
    {
        var intStatus = (int)statusCode;
        return (intStatus >= 200) && (intStatus <= 299);
    }
}

