namespace Valour.Shared.Models;

/// <summary>
/// A purchase made through Google Play Billing that the Android app reports to
/// the server for verification.
/// </summary>
public class GooglePlayPurchaseRequest
{
    /// <summary>The Google Play product ID that was bought.</summary>
    public string ProductId { get; set; }

    /// <summary>The purchase token Google Play returned for the purchase.</summary>
    public string PurchaseToken { get; set; }
}

public static class GooglePlayProducts
{
    /// <summary>
    /// Valour Credit packs sold as consumable in-app products, mapped to the
    /// number of credits each one deposits.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> CreditPacks = new Dictionary<string, int>
    {
        ["vc_500"] = 500,
        ["vc_1000"] = 1000,
        ["vc_2000"] = 2000,
    };
}
