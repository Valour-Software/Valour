namespace Valour.Config.Configs;

public class CdnConfig
{
    /// <summary>
    /// The static instance of the current instance
    /// </summary>
    public static CdnConfig Current;

    public CdnConfig()
    {
        Current = this;
    }

    // Cross-server authorization
    public string Key { get; set; }

    // S3 properties
    public string S3Access { get; set; }
    public string S3Secret { get; set; }
    public string S3Endpoint { get; set; }
    
    // Public S3 properties
    public string PublicS3Access { get; set; }
    public string PublicS3Secret { get; set; }
    public string PublicS3Endpoint { get; set; }
}


