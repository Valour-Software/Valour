using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Client.Device;

/// <summary>
/// Purchases through an app store's own billing system. Store builds that must
/// use the store's checkout register this service, and the subscriptions page
/// then sells through it instead of Stripe. Other builds do not register it.
/// </summary>
public interface IStoreBillingService
{
    /// <summary>The store's name as shown to people, for example "Google Play".</summary>
    string StoreName { get; }

    /// <summary>The store's product ID for a subscription tier.</summary>
    string GetProductId(UserSubscriptionType tier);

    /// <summary>Valour Credit packs the store sells, keyed by product ID.</summary>
    IReadOnlyDictionary<string, int> CreditPacks { get; }

    /// <summary>True when this store bills the given Valour subscription.</summary>
    bool Bills(UserSubscription subscription);

    /// <summary>
    /// True when the store expects a "Restore purchases" button, as the App
    /// Store does for subscriptions bought on another device.
    /// </summary>
    bool SupportsRestore { get; }

    /// <summary>
    /// Returns the store's localized price for each product it sells, keyed by
    /// product ID. Products the store does not offer are left out.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetPricesAsync();

    /// <summary>
    /// Opens the store's checkout. For a subscription, any subscription the
    /// account already holds in the store is replaced. Completes when the
    /// person finishes or leaves checkout.
    /// </summary>
    /// <param name="accountId">The Valour user ID, attached to the purchase so the server can link it.</param>
    Task<StorePurchaseResult> PurchaseAsync(string productId, bool isSubscription, long accountId);

    /// <summary>
    /// Returns paid purchases the server has not finished processing, so they
    /// can be reported again.
    /// </summary>
    Task<IReadOnlyList<StorePurchase>> GetUnfinishedPurchasesAsync();

    /// <summary>
    /// Asks the store for purchases made with this store account, including
    /// on other devices, so they can be reported again.
    /// </summary>
    Task<IReadOnlyList<StorePurchase>> RestorePurchasesAsync();

    /// <summary>
    /// Reports a purchase to the server for verification. Once the server
    /// accepts it, the store is told the purchase was delivered.
    /// </summary>
    Task<TaskResult> ReportAsync(ValourClient client, StorePurchase purchase);

    /// <summary>Opens the store's page for managing a subscription.</summary>
    Task OpenManageSubscriptionAsync(string productId);
}

public record StorePurchase(string ProductId, string PurchaseToken, bool IsSubscription);

public enum StorePurchaseStatus
{
    Purchased,
    Pending,
    Cancelled,
    Failed,
}

public record StorePurchaseResult(StorePurchaseStatus Status, StorePurchase Purchase = null, string Message = null);
