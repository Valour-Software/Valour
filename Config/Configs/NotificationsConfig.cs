using WebPush;

namespace Valour.Config.Configs;

public class NotificationsConfig
{
    public static NotificationsConfig Current;
    private static VapidDetails _details;

    public NotificationsConfig()
    {
        Current = this;
    }

    public VapidDetails GetDetails()
    {
        if (_details == null)
        {
            _details = new VapidDetails(Subject, PublicKey, PrivateKey);
        }

        return _details;
    }

    public string Subject { get; set; }

    public string PublicKey { get; set; }

    public string PrivateKey { get; set; }
    
    public string AzureConnectionString { get; set; }
    public string AzureHubName { get; set; }
}

