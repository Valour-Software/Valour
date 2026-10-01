using Android.BillingClient.Api;
using Valour.Client.Device;
using Valour.Shared.Models;
using BillingClient = Android.BillingClient.Api.BillingClient;

namespace Valour.Client.Maui;

/// <summary>
/// Sells Stargazer subscriptions and Valour Credits through Google Play
/// Billing. Registered only in Play Store builds, where Play policy requires
/// its checkout for digital goods. The server verifies, acknowledges and
/// consumes every purchase; this class only runs checkout and reports what
/// Google Play returned.
/// </summary>
public class GooglePlayStoreBillingService : Java.Lang.Object, IStoreBillingService, IPurchasesUpdatedListener
{
    private const string SubscriptionType = "subs";
    private const string OneTimeType = "inapp";

    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly Dictionary<string, ProductDetails> _products = new();

    private BillingClient _client;
    private TaskCompletionSource<StorePurchaseResult> _checkout;
    private string _checkoutProductId;
    private bool _checkoutIsSubscription;

    public string StoreName => "Google Play";

    private async Task<BillingClient> ConnectAsync()
    {
        await _connectLock.WaitAsync();
        try
        {
            if (_client is { IsReady: true })
                return _client;

            _client ??= BillingClient.NewBuilder(Platform.AppContext)
                .SetListener(this)
                .EnablePendingPurchases(PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build())
                .EnableAutoServiceReconnection()
                .Build();

            var result = await _client.StartConnectionAsync();
            return result.ResponseCode == BillingResponseCode.Ok ? _client : null;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task LoadProductsAsync(BillingClient client)
    {
        if (_products.Count > 0)
            return;

        var subscriptions = UserSubscriptionTypes.TypeMap.Values
            .Where(x => x.GooglePlayProductId is not null)
            .Select(x => BuildProduct(x.GooglePlayProductId, SubscriptionType))
            .ToList();

        var credits = GooglePlayProducts.CreditPacks.Keys
            .Select(x => BuildProduct(x, OneTimeType))
            .ToList();

        // A single query cannot mix product types.
        foreach (var products in new[] { subscriptions, credits })
        {
            var result = await client.QueryProductDetailsAsync(
                QueryProductDetailsParams.NewBuilder().SetProductList(products).Build());

            if (result.Result.ResponseCode != BillingResponseCode.Ok || result.ProductDetails is null)
                continue;

            foreach (var details in result.ProductDetails)
                _products[details.ProductId] = details;
        }
    }

    private static QueryProductDetailsParams.Product BuildProduct(string productId, string type) =>
        QueryProductDetailsParams.Product.NewBuilder().SetProductId(productId).SetProductType(type).Build();

    public async Task<IReadOnlyDictionary<string, string>> GetPricesAsync()
    {
        var prices = new Dictionary<string, string>();

        var client = await ConnectAsync();
        if (client is null)
            return prices;

        await LoadProductsAsync(client);

        foreach (var (productId, details) in _products)
        {
            var price = details.ProductType == SubscriptionType
                ? details.GetSubscriptionOfferDetails()?.FirstOrDefault()?.PricingPhases.PricingPhaseList.LastOrDefault()?.FormattedPrice
                : details.GetOneTimePurchaseOfferDetails()?.FormattedPrice;

            if (price is not null)
                prices[productId] = price;
        }

        return prices;
    }

    public async Task<StorePurchaseResult> PurchaseAsync(string productId, bool isSubscription, long accountId)
    {
        var client = await ConnectAsync();
        if (client is null)
            return new StorePurchaseResult(StorePurchaseStatus.Failed, Message: "Google Play is not available on this device.");

        await LoadProductsAsync(client);
        if (!_products.TryGetValue(productId, out var details))
            return new StorePurchaseResult(StorePurchaseStatus.Failed, Message: "This item is not available in Google Play right now.");

        if (Platform.CurrentActivity is not { } activity)
            return new StorePurchaseResult(StorePurchaseStatus.Failed, Message: "Could not open Google Play checkout.");

        var productParams = BillingFlowParams.ProductDetailsParams.NewBuilder().SetProductDetails(details);
        if (isSubscription)
        {
            var offerToken = details.GetSubscriptionOfferDetails()?.FirstOrDefault()?.OfferToken;
            if (offerToken is null)
                return new StorePurchaseResult(StorePurchaseStatus.Failed, Message: "This plan is not available in Google Play right now.");

            productParams.SetOfferToken(offerToken);
        }

        var flow = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList([productParams.Build()])
            .SetObfuscatedAccountId(accountId.ToString());

        if (isSubscription)
        {
            // Switching plans replaces the current Google Play purchase. The
            // remaining time on the old plan is credited toward the new one.
            var current = await GetOwnedSubscriptionAsync(client);
            if (current is not null)
            {
                if (current.Products.Contains(productId))
                    return new StorePurchaseResult(StorePurchaseStatus.Failed, Message: "You already have this plan in Google Play.");

                flow.SetSubscriptionUpdateParams(BillingFlowParams.SubscriptionUpdateParams.NewBuilder()
                    .SetOldPurchaseToken(current.PurchaseToken)
                    .SetSubscriptionReplacementMode(BillingFlowParams.SubscriptionUpdateParams.ReplacementMode.WithTimeProration)
                    .Build());
            }
        }

        _checkout?.TrySetResult(new StorePurchaseResult(StorePurchaseStatus.Cancelled));
        var checkout = new TaskCompletionSource<StorePurchaseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _checkout = checkout;
        _checkoutProductId = productId;
        _checkoutIsSubscription = isSubscription;

        var launch = await MainThread.InvokeOnMainThreadAsync(() => client.LaunchBillingFlow(activity, flow.Build()));
        if (launch.ResponseCode != BillingResponseCode.Ok)
        {
            _checkout = null;
            return MapFailure(launch);
        }

        return await checkout.Task;
    }

    /// <summary>
    /// Called by Google Play when checkout ends, and later when a pending
    /// payment completes.
    /// </summary>
    public void OnPurchasesUpdated(BillingResult result, IList<Purchase> purchases)
    {
        var checkout = _checkout;
        _checkout = null;
        if (checkout is null)
            return;

        if (result.ResponseCode != BillingResponseCode.Ok)
        {
            checkout.TrySetResult(MapFailure(result));
            return;
        }

        var purchase = purchases?.FirstOrDefault(x => x.Products.Contains(_checkoutProductId));
        if (purchase is null)
        {
            checkout.TrySetResult(new StorePurchaseResult(StorePurchaseStatus.Failed, Message: "Google Play did not return the purchase."));
            return;
        }

        var storePurchase = new StorePurchase(_checkoutProductId, purchase.PurchaseToken, _checkoutIsSubscription);
        checkout.TrySetResult(purchase.PurchaseState == PurchaseState.Purchased
            ? new StorePurchaseResult(StorePurchaseStatus.Purchased, storePurchase)
            : new StorePurchaseResult(StorePurchaseStatus.Pending, storePurchase,
                "Your payment is pending. Your purchase will arrive when it completes."));
    }

    public async Task<IReadOnlyList<StorePurchase>> GetUnfinishedPurchasesAsync()
    {
        var unfinished = new List<StorePurchase>();

        var client = await ConnectAsync();
        if (client is null)
            return unfinished;

        // The server acknowledges subscriptions and consumes credit packs, so
        // anything still in this state was never reported successfully.
        foreach (var purchase in await QueryPurchasesAsync(client, SubscriptionType))
        {
            if (purchase.PurchaseState == PurchaseState.Purchased && !purchase.IsAcknowledged)
                unfinished.Add(new StorePurchase(purchase.Products.First(), purchase.PurchaseToken, true));
        }

        foreach (var purchase in await QueryPurchasesAsync(client, OneTimeType))
        {
            if (purchase.PurchaseState == PurchaseState.Purchased)
                unfinished.Add(new StorePurchase(purchase.Products.First(), purchase.PurchaseToken, false));
        }

        return unfinished;
    }

    private static async Task<Purchase> GetOwnedSubscriptionAsync(BillingClient client) =>
        (await QueryPurchasesAsync(client, SubscriptionType))
            .FirstOrDefault(x => x.PurchaseState == PurchaseState.Purchased);

    private static async Task<IList<Purchase>> QueryPurchasesAsync(BillingClient client, string type)
    {
        var result = await client.QueryPurchasesAsync(QueryPurchasesParams.NewBuilder().SetProductType(type).Build());
        return result.Result.ResponseCode == BillingResponseCode.Ok && result.Purchases is not null
            ? result.Purchases
            : [];
    }

    public Task OpenManageSubscriptionAsync(string productId)
    {
        var uri = $"https://play.google.com/store/account/subscriptions?sku={Uri.EscapeDataString(productId ?? "")}" +
                  $"&package={Uri.EscapeDataString(AppInfo.Current.PackageName)}";
        return Launcher.Default.OpenAsync(uri);
    }

    private static StorePurchaseResult MapFailure(BillingResult result) => result.ResponseCode switch
    {
        BillingResponseCode.UserCancelled => new StorePurchaseResult(StorePurchaseStatus.Cancelled),
        BillingResponseCode.ItemAlreadyOwned => new StorePurchaseResult(StorePurchaseStatus.Failed,
            Message: "You already own this item. Reopen this page to finish adding it to your account."),
        BillingResponseCode.NetworkError or BillingResponseCode.ServiceUnavailable or BillingResponseCode.ServiceTimeout =>
            new StorePurchaseResult(StorePurchaseStatus.Failed, Message: "Could not reach Google Play. Check your connection and try again."),
        _ => new StorePurchaseResult(StorePurchaseStatus.Failed, Message: "Google Play could not complete the purchase."),
    };
}
