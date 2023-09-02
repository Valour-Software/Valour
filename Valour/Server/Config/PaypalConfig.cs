namespace Valour.Server.Config;

/// <summary>
/// Please don't leak this one :)
/// </summary>
public class PaypalConfig
{
    /// <summary>
    /// The static instance of the current instance
    /// </summary>
    public static PaypalConfig Current;
    
    public PaypalConfig()
    {
        Current = this;
    }
    
    public string ClientId { get; set; }
    public string AppSecret { get; set; }
    public string MerchantId { get; set; }
}