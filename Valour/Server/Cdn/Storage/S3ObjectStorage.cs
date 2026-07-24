using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Valour.Shared;
using Valour.Shared.Cdn;

namespace Valour.Server.Cdn.Storage;

/// <summary>
/// S3-compatible object storage (Cloudflare R2, MinIO, Garage, AWS S3, ...).
/// Payload signing is disabled for R2 compatibility.
/// </summary>
public class S3ObjectStorage : IObjectStorage
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(750),
        TimeSpan.FromMilliseconds(1500)
    };

    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly ILogger _logger;

    public bool SupportsSignedUrls => true;

    public S3ObjectStorage(AmazonS3Client client, string bucket, ILogger logger)
    {
        _client = client;
        _bucket = bucket;
        _logger = logger;
    }

    public async Task<TaskResult> PutAsync(string key, Stream data, string contentType)
    {
        var request = new PutObjectRequest
        {
            Key = key,
            InputStream = data,
            BucketName = _bucket,
            DisablePayloadSigning = true,
            ContentType = contentType
        };

        PutObjectResponse response;
        try
        {
            response = await PutObjectWithRetryAsync(request, key);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to upload object to bucket: {Key}", key);
            return new TaskResult(false, "Failed to upload object to bucket.");
        }

        if (!CdnUtils.IsSuccessStatusCode(response.HttpStatusCode))
            return new TaskResult(false, $"Failed to PUT object into bucket. ({response.HttpStatusCode})");

        return TaskResult.SuccessResult;
    }

    public async Task<ObjectStorageDownload> GetAsync(string key)
    {
        try
        {
            var response = await _client.GetObjectAsync(new GetObjectRequest
            {
                Key = key,
                BucketName = _bucket,
            });

            if (!CdnUtils.IsSuccessStatusCode(response.HttpStatusCode))
            {
                response.Dispose();
                return null;
            }

            return new ObjectStorageDownload(response.ResponseStream, response);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to get object from bucket: {Key}", key);
            return null;
        }
    }

    public async Task<TaskResult> DeleteAsync(string key)
    {
        try
        {
            var response = await _client.DeleteObjectAsync(new DeleteObjectRequest
            {
                Key = key,
                BucketName = _bucket,
            });

            if (CdnUtils.IsSuccessStatusCode(response.HttpStatusCode))
                return TaskResult.SuccessResult;

            return TaskResult.FromFailure($"Failed to delete object from bucket. ({response.HttpStatusCode})");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete object from bucket: {Key}", key);
            return TaskResult.FromFailure("Failed to delete object from bucket.");
        }
    }

    public async Task<string> GetSignedUrlAsync(string key, string mimeType, string fileName, TimeSpan expiry)
    {
        // Uploads store the client-supplied MIME type. Serving HTML or SVG
        // inline would run attacker script on a Valour origin, so active
        // content is downgraded to an opaque download. Uploading these is also
        // blocked, but this covers anything stored before that check existed.
        var isActiveContent = CdnUtils.IsActiveContentUpload(fileName, mimeType);

        var request = new GetPreSignedUrlRequest
        {
            Key = key,
            BucketName = _bucket,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentType = isActiveContent ? "application/octet-stream" : mimeType,
                ContentDisposition = isActiveContent
                    ? $"attachment; filename=\"{EscapeHeaderFileName(fileName)}\""
                    : $"inline; filename=\"{EscapeHeaderFileName(fileName)}\""
            }
        };

        return await _client.GetPreSignedURLAsync(request);
    }

    /// <summary>
    /// Makes a user-supplied filename safe to embed in a quoted header value.
    /// Quotes, backslashes, and control characters would otherwise let the
    /// filename break out and inject extra header directives.
    /// </summary>
    private static string EscapeHeaderFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return "download";

        var builder = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            if (c is '"' or '\\' || char.IsControl(c))
                continue;

            builder.Append(c);
        }

        return builder.Length == 0 ? "download" : builder.ToString();
    }

    private async Task<PutObjectResponse> PutObjectWithRetryAsync(PutObjectRequest request, string key)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (request.InputStream.CanSeek)
                {
                    request.InputStream.Position = 0;
                }

                return await _client.PutObjectAsync(request);
            }
            catch (Exception ex) when (ShouldRetryPut(ex) && attempt < RetryDelays.Length)
            {
                var delay = RetryDelays[attempt];
                _logger.LogWarning(
                    ex,
                    "Transient S3 upload error for {Key}. Retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts})",
                    key,
                    (int)delay.TotalMilliseconds,
                    attempt + 1,
                    RetryDelays.Length + 1);

                await Task.Delay(delay);
            }
        }
    }

    private static bool ShouldRetryPut(Exception ex)
    {
        if (ex is AmazonS3Exception s3Ex)
        {
            if (s3Ex.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.InternalServerError)
                return true;

            if (string.Equals(s3Ex.ErrorCode, "SlowDown", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("Reduce your concurrent request rate for the same object", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Slow Down", StringComparison.OrdinalIgnoreCase);
    }
}
