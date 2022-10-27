namespace Valour.Server.Config;

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

    // Database properties
    public string DbAddress { get; set; }
    public string DbUser { get; set; }
    public string DbPassword { get; set; }
    public string DbName { get; set; }

    // S3 properties
    public string S3Access { get; set; }
    public string S3Secret { get; set; }
    public string S3Endpoint { get; set; }
}


