namespace Valour.Config.Configs;

public class NotificationsConfig
{
    public static NotificationsConfig Current;

    public NotificationsConfig()
    {
        Current = this;
    }

    public string Subject { get; set; }

    public string PublicKey { get; set; }

    public string PrivateKey { get; set; }
    
    public string AzureConnectionString { get; set; }
    public string AzureHubName { get; set; }

    public string FirebaseCredentialPath { get; set; }
}

