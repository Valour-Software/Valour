namespace Valour.Shared.Models;

public class PushNotificationsRequest
{
    public string Endpoint { get; set; }
    public string Key { get; set; }
    public string Auth { get; set; }
}