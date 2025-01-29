namespace Valour.Server.Models;

public class WebPushSubscription
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Endpoint { get; set; }
    public string Key { get; set; }
    public string Auth { get; set; }
}