using Foundation;
using StoreKit;
using Valour.Client.Device;
using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Client.Maui;

/// <summary>
/// Sells Stargazer subscriptions and Valour Credits through App Store In-App
/// Purchase, which App Store rules require for digital goods in the iOS app.
/// The server verifies every purchase with the App Store Server API; this
/// class runs checkout, reports transactions, and finishes each one only after
/// the server accepts it, so StoreKit keeps offering an unreported purchase
/// until it arrives.
///
/// .NET exposes the original StoreKit API rather than StoreKit 2. Its purchase
/// field applicationUsername is passed to the server as the app account token
/// when it is a UUID, which links each purchase to its Valour account.
/// </summary>
#pragma warning disable CA1422 // StoreKit 1 is deprecated in iOS 18 but remains supported.
public class AppStoreBillingService : SKPaymentTransactionObserver, IStoreBillingService
{
    private readonly Dictionary<string, SKProduct> _products = new();
    private readonly SemaphoreSlim _productsLock = new(1, 1);

    private TaskCompletionSource<StorePurchaseResult>? _checkout;
    private string? _checkoutProductId;
    private TaskCompletionSource<bool>? _restore;

    public AppStoreBillingService()
    {
        // Observing from launch lets StoreKit deliver purchases that finished
        // while the app was closed.
        SKPaymentQueue.DefaultQueue.AddTransactionObserver(this);
    }

    public string StoreName => "the App Store";

    public string GetProductId(UserSubscriptionType tier) => tier.AppStoreProductId;

    public IReadOnlyDictionary<string, int> CreditPacks => AppStoreProducts.CreditPacks;

    public bool Bills(Valour.Sdk.Models.UserSubscription subscription) =>
        subscription?.AppStoreOriginalTransactionId is not null;

    public bool SupportsRestore => true;

    private static IEnumerable<string> AllProductIds =>
        UserSubscriptionTypes.TypeMap.Values.Select(x => x.AppStoreProductId)
            .Concat(AppStoreProducts.CreditPacks.Keys)
            .Where(x => x is not null);

    private static bool IsSubscriptionProduct(string productId) =>
        UserSubscriptionTypes.FromAppStoreProductId(productId) is not null;

    #region Products

    private async Task LoadProductsAsync()
    {
        await _productsLock.WaitAsync();
        try
        {
            if (_products.Count > 0)
                return;

            var ids = new NSSet<NSString>(AllProductIds.Select(x => new NSString(x)).ToArray());
            var request = new SKProductsRequest(ids);
            var loader = new ProductsLoader();
            request.Delegate = loader;
            request.Start();

            foreach (var product in await loader.Task)
                _products[product.ProductIdentifier] = product;
        }
        finally
        {
            _productsLock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPricesAsync()
    {
        await LoadProductsAsync();

        var prices = new Dictionary<string, string>();
        foreach (var (id, product) in _products)
        {
            var formatter = new NSNumberFormatter
            {
                NumberStyle = NSNumberFormatterStyle.Currency,
                Locale = product.PriceLocale,
            };
            prices[id] = formatter.StringFromNumber(product.Price);
        }

        return prices;
    }

    private sealed class ProductsLoader : SKProductsRequestDelegate
    {
        private readonly TaskCompletionSource<SKProduct[]> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SKProduct[]> Task => _tcs.Task;

        public override void ReceivedResponse(SKProductsRequest request, SKProductsResponse response) =>
            _tcs.TrySetResult(response.Products ?? []);

        public override void RequestFailed(SKRequest request, NSError error) =>
            _tcs.TrySetResult([]);
    }

    #endregion

    #region Checkout

    public async Task<StorePurchaseResult> PurchaseAsync(string productId, bool isSubscription, long accountId)
    {
        if (!SKPaymentQueue.CanMakePayments)
            return new StorePurchaseResult(StorePurchaseStatus.Failed,
                Message: "Purchases are turned off on this device, for example by Screen Time.");

        await LoadProductsAsync();
        if (!_products.TryGetValue(productId, out var product))
            return new StorePurchaseResult(StorePurchaseStatus.Failed, Message: "This item is not available in the App Store right now.");

        _checkout?.TrySetResult(new StorePurchaseResult(StorePurchaseStatus.Cancelled));
        _checkout = new TaskCompletionSource<StorePurchaseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _checkoutProductId = productId;

        var payment = SKMutablePayment.PaymentWithProduct(product);
        payment.ApplicationUsername = AppStoreAccountToken.FromUserId(accountId).ToString();
        SKPaymentQueue.DefaultQueue.AddPayment(payment);

        return await _checkout.Task;
    }

    public override void UpdatedTransactions(SKPaymentQueue queue, SKPaymentTransaction[] transactions)
    {
        foreach (var transaction in transactions)
        {
            var productId = transaction.Payment?.ProductIdentifier;
            var isCheckout = _checkout is not null && productId == _checkoutProductId;

            switch (transaction.TransactionState)
            {
                case SKPaymentTransactionState.Purchased:
                    // Finished later, once the server has accepted it.
                    if (isCheckout)
                        CompleteCheckout(new StorePurchaseResult(StorePurchaseStatus.Purchased, ToStorePurchase(transaction)));
                    break;

                case SKPaymentTransactionState.Deferred:
                    // Waiting for approval, for example Ask to Buy. StoreKit
                    // delivers it as Purchased later.
                    if (isCheckout)
                        CompleteCheckout(new StorePurchaseResult(StorePurchaseStatus.Pending,
                            Message: "Your purchase is waiting for approval. It will be added to your account once it is approved."));
                    break;

                case SKPaymentTransactionState.Failed:
                    queue.FinishTransaction(transaction);
                    if (isCheckout)
                        CompleteCheckout(MapFailure(transaction.Error));
                    break;
            }
        }
    }

    private void CompleteCheckout(StorePurchaseResult result)
    {
        _checkout?.TrySetResult(result);
        _checkout = null;
        _checkoutProductId = null;
    }

    private static StorePurchase ToStorePurchase(SKPaymentTransaction transaction)
    {
        var productId = transaction.Payment.ProductIdentifier;
        return new StorePurchase(productId, transaction.TransactionIdentifier ?? "", IsSubscriptionProduct(productId));
    }

    private static StorePurchaseResult MapFailure(NSError? error)
    {
        if (error is not null && error.Domain == "SKErrorDomain" && error.Code == (nint)(long)SKError.PaymentCancelled)
            return new StorePurchaseResult(StorePurchaseStatus.Cancelled);

        return new StorePurchaseResult(StorePurchaseStatus.Failed,
            Message: error?.LocalizedDescription ?? "The App Store could not complete the purchase.");
    }

    #endregion

    #region Reporting

    public Task<IReadOnlyList<StorePurchase>> GetUnfinishedPurchasesAsync()
    {
        IReadOnlyList<StorePurchase> unfinished = SKPaymentQueue.DefaultQueue.Transactions
            .Where(x => x.TransactionState is SKPaymentTransactionState.Purchased or SKPaymentTransactionState.Restored)
            .Select(ToStorePurchase)
            .ToList();

        return Task.FromResult(unfinished);
    }

    public async Task<IReadOnlyList<StorePurchase>> RestorePurchasesAsync()
    {
        _restore?.TrySetResult(false);
        _restore = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SKPaymentQueue.DefaultQueue.RestoreCompletedTransactions();
        await _restore.Task;

        return await GetUnfinishedPurchasesAsync();
    }

    public override void RestoreCompletedTransactionsFinished(SKPaymentQueue queue) => _restore?.TrySetResult(true);

    public override void RestoreCompletedTransactionsFailedWithError(SKPaymentQueue queue, NSError error) =>
        _restore?.TrySetResult(false);

    public async Task<TaskResult> ReportAsync(ValourClient client, StorePurchase purchase)
    {
        var result = purchase.IsSubscription
            ? await client.SubscriptionService.ClaimAppStoreSubscriptionAsync(purchase.ProductId, purchase.PurchaseToken)
            : await client.SubscriptionService.ClaimAppStoreCreditsAsync(purchase.ProductId, purchase.PurchaseToken);

        var restored = SKPaymentQueue.DefaultQueue.Transactions
            .Any(x => x.TransactionIdentifier == purchase.PurchaseToken && x.TransactionState == SKPaymentTransactionState.Restored);

        // A restored transaction is a copy of one already delivered, so it is
        // finished either way. A new purchase stays open until the server has it.
        if (result.Success || restored)
            Finish(purchase.PurchaseToken);

        return result;
    }

    private static void Finish(string transactionId)
    {
        foreach (var transaction in SKPaymentQueue.DefaultQueue.Transactions)
        {
            if (transaction.TransactionIdentifier == transactionId)
                SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);
        }
    }

    #endregion

    public Task OpenManageSubscriptionAsync(string productId) =>
        Launcher.Default.OpenAsync("https://apps.apple.com/account/subscriptions");
}
#pragma warning restore CA1422
