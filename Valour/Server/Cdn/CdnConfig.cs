namespace Valour.Server.Cdn;

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

    [JsonPropertyName("auth_key")]
    public string AuthKey { get; set; }

    // Database properties

    [JsonPropertyName("db_address")]
    public string DbAddr { get; set; }

    [JsonPropertyName("db_user")]
    public string DbUser { get; set; }

    [JsonPropertyName("db_pass")]
    public string DbPass { get; set; }

    [JsonPropertyName("db_name")]
    public string DbName { get; set; }

    [JsonPropertyName("s3_access")]
    public string S3Access { get; set; }
    [JsonPropertyName("s3_secret")]
    public string S3Secret { get; set; }
    [JsonPropertyName("r2_endpoint")]
    public string R2Endpoint { get; set; }
}


