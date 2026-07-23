namespace Valour.Config.Configs;

public class StripeConfig
{
    public static StripeConfig? Current;

    public StripeConfig()
    {
        Current = this;
    }

    public string? SecretKey { get; set; }
    public string? ApiVersion { get; set; }
    public string? WebhookSecret { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
    public string? SubscriptionSuccessUrl { get; set; }
    public string? SubscriptionCancelUrl { get; set; }
}
