using Amazon.Runtime;

namespace Valour.Server.Cdn;

/// <summary>
/// HTTP clients for S3 endpoints chosen by users (planet-owned storage). The
/// AWS SDK otherwise builds its own handler, which resolves DNS again at
/// connect time and follows redirects, so a URL validated at save time could
/// still reach an internal address. These clients dial only validated public
/// addresses and never follow redirects.
/// </summary>
public sealed class SsrfSafeS3HttpClientFactory : Amazon.Runtime.HttpClientFactory
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly bool _allowPrivate;

    public SsrfSafeS3HttpClientFactory(bool allowPrivate)
    {
        _allowPrivate = allowPrivate;
    }

    public override HttpClient CreateHttpClient(IClientConfig clientConfig)
    {
        return new HttpClient(SsrfSafeConnect.CreateHandler(_allowPrivate))
        {
            Timeout = clientConfig.Timeout ?? DefaultTimeout
        };
    }

    // Cache per SDK client (a null key), so the HttpClient is disposed with
    // the AmazonS3Client that created it rather than shared across planets.
    public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => true;

    public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;

    public override string GetConfigUniqueString(IClientConfig clientConfig) => null;
}
