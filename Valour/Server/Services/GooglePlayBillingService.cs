using System.Security.Cryptography;
using System.Text;
using Google;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.AndroidPublisher.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Valour.Config.Configs;
using Valour.Server.Api.Dynamic;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Verifies and fulfills purchases made through Google Play Billing in the
/// Android app. The app reports each purchase token after checkout, and Google
/// reports later changes (renewals, cancellations, refunds) through real-time
/// developer notifications. Both paths read the purchase back from the Google
/// Play Developer API and act only on what Google returns, so a token never
/// grants anything on the client's word alone.
///
/// The app sets the purchase's obfuscated account ID to the Valour user ID.
/// That links a purchase to its account even when the app closes before it
/// can report the purchase.
/// </summary>
public class GooglePlayBillingService
{
    private static readonly Lazy<AndroidPublisherService> Publisher = new(CreatePublisher);

    private readonly ValourDb _db;
    private readonly EcoService _ecoService;
    private readonly ILogger<GooglePlayBillingService> _logger;

    public GooglePlayBillingService(ValourDb db, EcoService ecoService, ILogger<GooglePlayBillingService> logger)
    {
        _db = db;
        _ecoService = ecoService;
        _logger = logger;
    }

    public static bool IsConfigured => GooglePlayConfig.Current?.IsConfigured == true;

    private static string PackageName => GooglePlayConfig.Current!.PackageName!;

    private static AndroidPublisherService CreatePublisher()
    {
        var credential = GoogleCredential
            .FromFile(GooglePlayConfig.Current!.ServiceAccountPath!)
            .CreateScoped(AndroidPublisherService.Scope.Androidpublisher);

        return new AndroidPublisherService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Valour",
        });
    }

    #region Subscriptions

    /// <summary>
    /// Verifies a subscription purchase the app reports for the signed-in user
    /// and records it.
    /// </summary>
    public async Task<TaskResult> ClaimSubscriptionAsync(long userId, string purchaseToken)
    {
        if (!IsConfigured)
            return TaskResult.FromFailure("Google Play billing is not available.");

        var purchase = await GetSubscriptionPurchaseAsync(purchaseToken);
        if (purchase is null)
            return TaskResult.FromFailure("Google Play does not recognize this purchase.");

        if (GetPurchaseUserId(purchase.ExternalAccountIdentifiers?.ObfuscatedExternalAccountId) != userId)
            return TaskResult.FromFailure("This purchase belongs to a different account.");

        return await SyncSubscriptionAsync(purchaseToken, purchase);
    }

    /// <summary>
    /// Reads a subscription from Google Play and brings the matching Valour
    /// subscription up to date. Used for notifications and periodic checks.
    /// </summary>
    public async Task<TaskResult> RefreshSubscriptionAsync(string purchaseToken)
    {
        var purchase = await GetSubscriptionPurchaseAsync(purchaseToken);
        if (purchase is null)
            return TaskResult.FromFailure("Google Play does not recognize this purchase.");

        return await SyncSubscriptionAsync(purchaseToken, purchase);
    }

    private async Task<SubscriptionPurchaseV2> GetSubscriptionPurchaseAsync(string purchaseToken)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken))
            return null;

        try
        {
            return await Publisher.Value.Purchases.Subscriptionsv2.Get(PackageName, purchaseToken).ExecuteAsync();
        }
        catch (GoogleApiException e) when (e.HttpStatusCode is System.Net.HttpStatusCode.NotFound
                                               or System.Net.HttpStatusCode.BadRequest
                                               or System.Net.HttpStatusCode.Gone)
        {
            return null;
        }
    }

    private async Task<TaskResult> SyncSubscriptionAsync(string purchaseToken, SubscriptionPurchaseV2 purchase)
    {
        var state = GooglePlaySubscriptionState.Read(purchase, DateTime.UtcNow);
        if (state is null)
        {
            _logger.LogWarning("Google Play subscription has unknown product {ProductId}",
                purchase.LineItems?.FirstOrDefault()?.ProductId);
            return TaskResult.FromFailure("Unknown subscription product.");
        }

        var sub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.GooglePlayPurchaseToken == purchaseToken);

        if (sub is null)
        {
            // Pending and lapsed purchases are recorded once they are paid.
            if (!state.Entitled)
                return TaskResult.FromFailure("This subscription is not active.");

            if (state.AccountUserId is null)
            {
                _logger.LogWarning("Google Play subscription has no Valour account ID and cannot be linked");
                return TaskResult.FromFailure("This purchase is not linked to a Valour account.");
            }

            var result = await CreateSubscriptionAsync(state.AccountUserId.Value, purchaseToken, state);
            if (!result.Success)
                return result.WithoutData();

            sub = result.Data;
        }
        else
        {
            await UpdateSubscriptionAsync(sub, state);
        }

        if (sub.Active && state.NeedsAcknowledgement)
            await AcknowledgeSubscriptionAsync(state.Tier.GooglePlayProductId, purchaseToken);

        return TaskResult.SuccessResult;
    }

    private async Task UpdateSubscriptionAsync(Valour.Database.UserSubscription sub, GooglePlaySubscriptionState state)
    {
        var user = await _db.Users.FindAsync(sub.UserId);

        sub.Type = state.Tier.Name;
        sub.Cancelled = !state.AutoRenewing;
        sub.StripePaymentFailed = state.PaymentFailed;
        sub.GooglePlayExpiry = state.Expiry;

        if (state.Entitled && !sub.Active)
        {
            // An account hold ends when payment recovers. The same token comes
            // back, so the subscription resumes unless another one has started
            // in the meantime (including the plan that replaced this one).
            var hasOtherActive = await _db.UserSubscriptions
                .AnyAsync(x => x.UserId == sub.UserId && x.Active && x.Id != sub.Id);
            if (!hasOtherActive)
            {
                sub.Active = true;
                if (user is not null)
                    user.SubscriptionType = state.Tier.Name;
            }
        }
        else if (!state.Entitled && sub.Active)
        {
            sub.Active = false;
            if (user is not null && user.SubscriptionType == sub.Type)
                user.SubscriptionType = null;
        }
        else if (state.Entitled && user is not null)
        {
            user.SubscriptionType = state.Tier.Name;
        }

        var renewed = state.IsRenewalOf(sub.GooglePlayOrderId);
        if (renewed)
        {
            sub.GooglePlayOrderId = state.OrderId;
            sub.LastCharged = DateTime.UtcNow;
            sub.Renewals += 1;
        }

        await _db.SaveChangesAsync();

        if (renewed)
        {
            await StripeApi.DepositVcRewardAsync(sub.UserId, state.Tier.VcReward,
                BuildFingerprint("google_play_reward", state.OrderId),
                $"Google Play {state.Tier.Name} Monthly Reward - Thank you!",
                _ecoService, _db, _logger);
        }
    }

    private async Task<TaskResult<Valour.Database.UserSubscription>> CreateSubscriptionAsync(
        long userId,
        string purchaseToken,
        GooglePlaySubscriptionState state)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult<Valour.Database.UserSubscription>.FromFailure("User not found.");

        var activeSubs = await _db.UserSubscriptions
            .Where(x => x.UserId == userId && x.Active)
            .ToListAsync();

        foreach (var active in activeSubs)
        {
            if (active.StripeSubscriptionId is not null)
            {
                // The app warns before Google Play checkout while a card
                // subscription is active, but cannot prevent buying both.
                _logger.LogWarning("User {UserId} bought a Google Play subscription while a Stripe subscription is active",
                    userId);
                continue;
            }

            // A plan change in Google Play replaces the old purchase, and a
            // Google Play purchase replaces a subscription paid with Valour Credits.
            if (active.GooglePlayPurchaseToken is null || active.GooglePlayPurchaseToken == state.LinkedPurchaseToken)
            {
                active.Active = false;
                active.PendingType = null;
            }
        }

        var now = DateTime.UtcNow;
        var sub = new Valour.Database.UserSubscription
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Type = state.Tier.Name,
            Active = true,
            Created = now,
            LastCharged = now,
            Renewals = 0,
            Cancelled = !state.AutoRenewing,
            StripePaymentFailed = state.PaymentFailed,
            GooglePlayPurchaseToken = purchaseToken,
            GooglePlayOrderId = state.OrderId,
            GooglePlayExpiry = state.Expiry,
        };

        _db.UserSubscriptions.Add(sub);
        user.SubscriptionType = state.Tier.Name;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The app's report and Google's notification arrived together and
            // the other one recorded the purchase first.
            _db.ChangeTracker.Clear();
            var existing = await _db.UserSubscriptions
                .FirstOrDefaultAsync(x => x.GooglePlayPurchaseToken == purchaseToken);
            return existing is null
                ? TaskResult<Valour.Database.UserSubscription>.FromFailure("Failed to record the subscription.")
                : TaskResult<Valour.Database.UserSubscription>.FromData(existing);
        }

        if (state.EarnsWelcomeReward)
        {
            await StripeApi.DepositVcRewardAsync(userId, state.Tier.VcReward,
                BuildFingerprint("google_play_reward", state.OrderId),
                $"Google Play {state.Tier.Name} Subscription Reward - Welcome!",
                _ecoService, _db, _logger);
        }

        return TaskResult<Valour.Database.UserSubscription>.FromData(sub);
    }

    private async Task AcknowledgeSubscriptionAsync(string productId, string purchaseToken)
    {
        // Google refunds purchases that are not acknowledged within three days.
        // A failure here is retried on the next notification or periodic check.
        try
        {
            await Publisher.Value.Purchases.Subscriptions
                .Acknowledge(new SubscriptionPurchasesAcknowledgeRequest(), PackageName, productId, purchaseToken)
                .ExecuteAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to acknowledge Google Play subscription {ProductId}", productId);
        }
    }

    /// <summary>
    /// Stops a Google Play subscription from renewing. It stays active until
    /// the paid period ends, as when the person cancels in Google Play.
    /// </summary>
    public async Task<TaskResult> CancelSubscriptionAsync(Valour.Database.UserSubscription sub)
    {
        var result = await StopRenewalAsync(sub, _logger);
        if (!result.Success)
            return result;

        sub.Cancelled = true;
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Tells Google Play not to renew a subscription. Does not change the
    /// Valour record, so account deletion can call it before removing data.
    /// </summary>
    public static async Task<TaskResult> StopRenewalAsync(Valour.Database.UserSubscription sub, ILogger logger)
    {
        if (!IsConfigured)
            return TaskResult.FromFailure("Google Play billing is not available.");

        if (!UserSubscriptionTypes.TypeMap.TryGetValue(sub.Type, out var tier) || tier.GooglePlayProductId is null)
            return TaskResult.FromFailure("Unknown subscription type.");

        try
        {
            await Publisher.Value.Purchases.Subscriptions
                .Cancel(PackageName, tier.GooglePlayProductId, sub.GooglePlayPurchaseToken)
                .ExecuteAsync();
        }
        catch (GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.Gone)
        {
            // Already expired; nothing left to renew.
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to cancel Google Play subscription {SubId}", sub.Id);
            return TaskResult.FromFailure("Failed to cancel the Google Play subscription.");
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Re-reads active Google Play subscriptions whose billing period has
    /// ended, in case a notification was missed.
    /// </summary>
    public async Task RefreshLapsedSubscriptionsAsync()
    {
        if (!IsConfigured)
            return;

        var cutoff = DateTime.UtcNow;
        var tokens = await _db.UserSubscriptions
            .Where(x => x.Active && x.GooglePlayPurchaseToken != null &&
                        (x.GooglePlayExpiry == null || x.GooglePlayExpiry < cutoff))
            .Select(x => x.GooglePlayPurchaseToken)
            .ToListAsync();

        foreach (var token in tokens)
        {
            try
            {
                var result = await RefreshSubscriptionAsync(token);
                if (!result.Success)
                {
                    // Google no longer knows the purchase, so it cannot renew.
                    var sub = await _db.UserSubscriptions.FirstOrDefaultAsync(x => x.GooglePlayPurchaseToken == token);
                    if (sub is { Active: true } && sub.GooglePlayExpiry < cutoff.AddDays(-7))
                    {
                        sub.Active = false;
                        var user = await _db.Users.FindAsync(sub.UserId);
                        if (user is not null && user.SubscriptionType == sub.Type)
                            user.SubscriptionType = null;
                        await _db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to refresh a Google Play subscription");
            }
        }
    }

    #endregion

    #region Valour Credits

    /// <summary>
    /// Verifies a Valour Credits purchase the app reports for the signed-in
    /// user, deposits the credits and consumes the purchase so it can be
    /// bought again.
    /// </summary>
    public async Task<TaskResult> ClaimCreditsAsync(long userId, string productId, string purchaseToken)
    {
        if (!IsConfigured)
            return TaskResult.FromFailure("Google Play billing is not available.");

        if (!GooglePlayProducts.CreditPacks.ContainsKey(productId ?? ""))
            return TaskResult.FromFailure("Unknown product.");

        var purchase = await GetProductPurchaseAsync(productId, purchaseToken);
        if (purchase is null)
            return TaskResult.FromFailure("Google Play does not recognize this purchase.");

        if (GetPurchaseUserId(purchase.ObfuscatedExternalAccountId) != userId)
            return TaskResult.FromFailure("This purchase belongs to a different account.");

        return await FulfillCreditsAsync(userId, productId, purchaseToken, purchase);
    }

    /// <summary>
    /// Fulfills a Valour Credits purchase reported by a notification.
    /// </summary>
    public async Task<TaskResult> RefreshCreditsAsync(string productId, string purchaseToken)
    {
        if (!GooglePlayProducts.CreditPacks.ContainsKey(productId ?? ""))
            return TaskResult.FromFailure("Unknown product.");

        var purchase = await GetProductPurchaseAsync(productId, purchaseToken);
        if (purchase is null)
            return TaskResult.FromFailure("Google Play does not recognize this purchase.");

        var userId = GetPurchaseUserId(purchase.ObfuscatedExternalAccountId);
        if (userId is null)
        {
            _logger.LogWarning("Google Play credit purchase has no Valour account ID and cannot be linked");
            return TaskResult.FromFailure("This purchase is not linked to a Valour account.");
        }

        return await FulfillCreditsAsync(userId.Value, productId, purchaseToken, purchase);
    }

    private async Task<ProductPurchase> GetProductPurchaseAsync(string productId, string purchaseToken)
    {
        if (string.IsNullOrWhiteSpace(purchaseToken))
            return null;

        try
        {
            return await Publisher.Value.Purchases.Products.Get(PackageName, productId, purchaseToken).ExecuteAsync();
        }
        catch (GoogleApiException e) when (e.HttpStatusCode is System.Net.HttpStatusCode.NotFound
                                               or System.Net.HttpStatusCode.BadRequest
                                               or System.Net.HttpStatusCode.Gone)
        {
            return null;
        }
    }

    private async Task<TaskResult> FulfillCreditsAsync(long userId, string productId, string purchaseToken, ProductPurchase purchase)
    {
        // 0 is purchased, 1 is canceled, 2 is pending (for example a cash payment).
        if (purchase.PurchaseState == 2)
            return new TaskResult(true, "Your payment is pending. Your credits will arrive when it completes.");

        if (purchase.PurchaseState != 0)
            return TaskResult.FromFailure("This purchase was canceled.");

        var credits = GooglePlayProducts.CreditPacks[productId];
        var fingerprint = BuildFingerprint("google_play_credits", purchaseToken);

        if (!await _db.Transactions.AnyAsync(x => x.Fingerprint == fingerprint))
        {
            await StripeApi.DepositVcRewardAsync(userId, credits, fingerprint,
                "Google Play Purchase - Thank you!", _ecoService, _db, _logger);

            if (!await _db.Transactions.AnyAsync(x => x.Fingerprint == fingerprint))
                return TaskResult.FromFailure("Failed to deposit your credits. They will be retried.");
        }

        // Consuming also acknowledges the purchase. Until it succeeds the app
        // keeps reporting the purchase, and the fingerprint prevents a second deposit.
        if (purchase.ConsumptionState != 1)
        {
            try
            {
                await Publisher.Value.Purchases.Products.Consume(PackageName, productId, purchaseToken).ExecuteAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to consume Google Play purchase of {ProductId}", productId);
            }
        }

        return TaskResult.SuccessResult;
    }

    #endregion

    #region Notifications

    /// <summary>
    /// Handles a real-time developer notification decoded from Pub/Sub.
    /// </summary>
    public async Task HandleNotificationAsync(GooglePlayNotification notification)
    {
        if (notification.PackageName is not null && notification.PackageName != PackageName)
        {
            _logger.LogWarning("Ignoring Google Play notification for package {PackageName}", notification.PackageName);
            return;
        }

        if (notification.SubscriptionNotification?.PurchaseToken is { } subToken)
        {
            await RefreshSubscriptionAsync(subToken);
        }
        else if (notification.OneTimeProductNotification is { PurchaseToken: { } productToken } productNotification)
        {
            // 1 is purchased; 2 is a pending purchase that was canceled.
            if (productNotification.NotificationType == 1)
                await RefreshCreditsAsync(productNotification.Sku, productToken);
        }
        else if (notification.VoidedPurchaseNotification?.PurchaseToken is { } voidedToken)
        {
            // Product type 1 is a subscription, 2 is a one-time product.
            if (notification.VoidedPurchaseNotification.ProductType == 1)
            {
                await RefreshSubscriptionAsync(voidedToken);
            }
            else
            {
                _logger.LogWarning("Google Play refunded a Valour Credits purchase (order {OrderId})",
                    notification.VoidedPurchaseNotification.OrderId);
            }
        }
        else if (notification.TestNotification is not null)
        {
            _logger.LogInformation("Received Google Play test notification");
        }
    }

    #endregion

    /// <summary>
    /// The app sets the obfuscated account ID to the Valour user ID.
    /// </summary>
    internal static long? GetPurchaseUserId(string obfuscatedAccountId) =>
        long.TryParse(obfuscatedAccountId, out var id) ? id : null;

    private static string BuildFingerprint(string prefix, string identifier)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier))).ToLowerInvariant();
        return $"{prefix}:{hash[..24]}";
    }
}

/// <summary>
/// What a Google Play subscription purchase means for Valour at a given time.
/// </summary>
public sealed record GooglePlaySubscriptionState(
    UserSubscriptionType Tier,
    bool Entitled,
    bool PaymentFailed,
    bool AutoRenewing,
    string OrderId,
    DateTime? Expiry,
    string LinkedPurchaseToken,
    long? AccountUserId,
    bool NeedsAcknowledgement)
{
    /// <summary>
    /// Reads a purchase from the Developer API. Returns null when the product
    /// is not a Stargazer tier.
    /// </summary>
    public static GooglePlaySubscriptionState Read(SubscriptionPurchaseV2 purchase, DateTime now)
    {
        var lineItem = purchase.LineItems?.FirstOrDefault();
        var tier = UserSubscriptionTypes.FromGooglePlayProductId(lineItem?.ProductId);
        if (lineItem is null || tier is null)
            return null;

        var expiry = lineItem.ExpiryTimeDateTimeOffset?.UtcDateTime;

        // A canceled subscription stays usable until the paid period ends, and
        // a subscription in its grace period stays usable while Google retries
        // the payment.
        var entitled = purchase.SubscriptionState is "SUBSCRIPTION_STATE_ACTIVE"
                           or "SUBSCRIPTION_STATE_IN_GRACE_PERIOD"
                           or "SUBSCRIPTION_STATE_CANCELED"
                       && expiry > now;

        return new GooglePlaySubscriptionState(
            tier,
            entitled,
            purchase.SubscriptionState is "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" or "SUBSCRIPTION_STATE_ON_HOLD",
            lineItem.AutoRenewingPlan?.AutoRenewEnabled == true,
            lineItem.LatestSuccessfulOrderId,
            expiry,
            purchase.LinkedPurchaseToken,
            GooglePlayBillingService.GetPurchaseUserId(purchase.ExternalAccountIdentifiers?.ObfuscatedExternalAccountId),
            purchase.AcknowledgementState == "ACKNOWLEDGEMENT_STATE_PENDING");
    }

    /// <summary>
    /// Each successful charge has its own order ID, so a new one means the
    /// subscription renewed since <paramref name="recordedOrderId"/>.
    /// </summary>
    public bool IsRenewalOf(string recordedOrderId) =>
        Entitled && OrderId is not null && OrderId != recordedOrderId;

    /// <summary>
    /// The first purchase of a subscription earns the welcome reward. A plan
    /// change replaces an existing purchase and does not, so switching plans
    /// back and forth cannot collect it repeatedly.
    /// </summary>
    public bool EarnsWelcomeReward => OrderId is not null && LinkedPurchaseToken is null;
}

/// <summary>
/// A Google Play real-time developer notification, as carried in the Pub/Sub
/// message data.
/// </summary>
public class GooglePlayNotification
{
    [JsonPropertyName("packageName")]
    public string PackageName { get; set; }

    [JsonPropertyName("subscriptionNotification")]
    public GooglePlaySubscriptionNotification SubscriptionNotification { get; set; }

    [JsonPropertyName("oneTimeProductNotification")]
    public GooglePlayProductNotification OneTimeProductNotification { get; set; }

    [JsonPropertyName("voidedPurchaseNotification")]
    public GooglePlayVoidedNotification VoidedPurchaseNotification { get; set; }

    [JsonPropertyName("testNotification")]
    public object TestNotification { get; set; }
}

public class GooglePlaySubscriptionNotification
{
    [JsonPropertyName("notificationType")]
    public int NotificationType { get; set; }

    [JsonPropertyName("purchaseToken")]
    public string PurchaseToken { get; set; }
}

public class GooglePlayProductNotification
{
    [JsonPropertyName("notificationType")]
    public int NotificationType { get; set; }

    [JsonPropertyName("purchaseToken")]
    public string PurchaseToken { get; set; }

    [JsonPropertyName("sku")]
    public string Sku { get; set; }
}

public class GooglePlayVoidedNotification
{
    [JsonPropertyName("purchaseToken")]
    public string PurchaseToken { get; set; }

    [JsonPropertyName("orderId")]
    public string OrderId { get; set; }

    [JsonPropertyName("productType")]
    public int ProductType { get; set; }
}
