using Valour.Sdk.Client;

namespace Valour.SDK.Services;

public class EcoService : ServiceBase
{
    private static readonly LogOptions LogOptions = new(
        "EcoService",
        "#0083ab",
        "#ab0055",
        "#ab8900"
    );
    
    private readonly ValourClient _client;
    public EcoService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }
}