using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Valour.Config.Configs;
using Valour.Server.Api.Dynamic;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Verifies and fulfills purchases made through App Store In-App Purchase in
/// the iOS app. The app reports each transaction ID after checkout, and Apple
/// reports later changes (renewals, expirations, refunds) through App Store
/// Server Notifications. Both paths read the purchase back from the App Store
/// Server API and act only on what Apple returns, so a transaction ID never
/// grants anything on the client's word alone.
///
/// The app sets each purchase's app account token to a UUID made from the
/// Valour user ID (see <see cref="AppStoreAccountToken"/>). That links a
/// purchase to its account even when the app closes before it can report it.
///
/// One App Store subscription keeps its original transaction ID across
/// renewals and plan changes within the Stargazer subscription group, so it is
/// one Valour subscription row. Apple has no API to stop a renewal: people
/// manage App Store subscriptions in their Apple account settings.
/// </summary>
public class AppStoreBillingService
{
    public const string HttpClientName = "AppStore";

    private const string ProductionUrl = "https://api.storekit.itunes.apple.com";
    private const string SandboxUrl = "https://api.storekit-sandbox.itunes.apple.com";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ValourDb _db;
    private readonly EcoService _ecoService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AppStoreBillingService> _logger;

    public AppStoreBillingService(
        ValourDb db,
        EcoService ecoService,
        IHttpClientFactory httpClientFactory,
        ILogger<AppStoreBillingService> logger)
    {
        _db = db;
        _ecoService = ecoService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public static bool IsConfigured => AppStoreConfig.Current?.IsConfigured == true;

    private static string BundleId => AppStoreConfig.Current!.BundleId!;

    #region Subscriptions

    /// <summary>
    /// Verifies a subscription transaction the app reports for the signed-in
    /// user and records it.
    /// </summary>
    public async Task<TaskResult> ClaimSubscriptionAsync(long userId, string transactionId)
    {
        if (!IsConfigured)
            return TaskResult.FromFailure("App Store billing is not available.");

        var status = await GetSubscriptionStatusAsync(transactionId);
        if (status is null)
            return TaskResult.FromFailure("The App Store does not recognize this purchase.");

        var state = AppStoreSubscriptionState.Read(status, DateTime.UtcNow);
        if (state is null)
            return TaskResult.FromFailure("Unknown subscription product.");

        if (state.AccountUserId != userId)
            return TaskResult.FromFailure("This purchase belongs to a different account.");

        return await SyncSubscriptionAsync(state);
    }

    /// <summary>
    /// Reads a subscription from the App Store and brings the matching Valour
    /// subscription up to date. Used for notifications and periodic checks.
    /// </summary>
    public async Task<TaskResult> RefreshSubscriptionAsync(string transactionId)
    {
        var status = await GetSubscriptionStatusAsync(transactionId);
        if (status is null)
            return TaskResult.FromFailure("The App Store does not recognize this purchase.");

        var state = AppStoreSubscriptionState.Read(status, DateTime.UtcNow);
        if (state is null)
        {
            _logger.LogWarning("App Store subscription has unknown product {ProductId}", status.Transaction?.ProductId);
            return TaskResult.FromFailure("Unknown subscription product.");
        }

        return await SyncSubscriptionAsync(state);
    }

    private async Task<TaskResult> SyncSubscriptionAsync(AppStoreSubscriptionState state)
    {
        var sub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.AppStoreOriginalTransactionId == state.OriginalTransactionId);

        if (sub is not null)
        {
            await UpdateSubscriptionAsync(sub, state);
            return TaskResult.SuccessResult;
        }

        // Lapsed purchases are recorded once they are paid again.
        if (!state.Entitled)
            return TaskResult.FromFailure("This subscription is not active.");

        if (state.AccountUserId is null)
        {
            _logger.LogWarning("App Store subscription has no Valour account token and cannot be linked");
            return TaskResult.FromFailure("This purchase is not linked to a Valour account.");
        }

        var result = await CreateSubscriptionAsync(state.AccountUserId.Value, state);
        return result.Success ? TaskResult.SuccessResult : result.WithoutData();
    }

    private async Task UpdateSubscriptionAsync(Valour.Database.UserSubscription sub, AppStoreSubscriptionState state)
    {
        var user = await _db.Users.FindAsync(sub.UserId);

        sub.Type = state.Tier.Name;
        sub.Cancelled = !state.AutoRenewing;
        sub.StripePaymentFailed = state.PaymentFailed;
        sub.AppStoreExpiry = state.Expiry;
        sub.PendingType = state.PendingTier?.Name;

        if (state.Entitled && !sub.Active)
        {
            // A resubscription or recovered payment brings the same original
            // transaction back, so the subscription resumes unless another one
            // has started in the meantime.
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
            if (user is not null)
                user.SubscriptionType = await SubscriptionService.GetRemainingTypeAsync(_db, sub.UserId, sub.Id);
        }
        else if (state.Entitled && user is not null)
        {
            user.SubscriptionType = state.Tier.Name;
        }

        var renewed = state.IsRenewalOf(sub.AppStoreTransactionId);
        if (state.TransactionId != sub.AppStoreTransactionId)
            sub.AppStoreTransactionId = state.TransactionId;

        if (renewed)
        {
            sub.LastCharged = DateTime.UtcNow;
            sub.Renewals += 1;
        }

        await _db.SaveChangesAsync();

        if (renewed)
        {
            await StripeApi.DepositVcRewardAsync(sub.UserId, state.Tier.VcReward,
                BuildFingerprint("app_store_reward", state.TransactionId),
                $"App Store {state.Tier.Name} Monthly Reward - Thank you!",
                _ecoService, _db, _logger);
        }
    }

    private async Task<TaskResult<Valour.Database.UserSubscription>> CreateSubscriptionAsync(
        long userId,
        AppStoreSubscriptionState state)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult<Valour.Database.UserSubscription>.FromFailure("User not found.");

        var activeSubs = await _db.UserSubscriptions
            .Where(x => x.UserId == userId && x.Active)
            .ToListAsync();

        foreach (var active in activeSubs)
        {
            if (active.StripeSubscriptionId is not null ||
                active.GooglePlayPurchaseToken is not null ||
                active.AppStoreOriginalTransactionId is not null)
            {
                // The app warns before checkout while another paid subscription
                // is active, but cannot prevent buying both.
                _logger.LogWarning("User {UserId} bought an App Store subscription while another paid subscription is active",
                    userId);
                continue;
            }

            // An App Store purchase replaces a subscription paid with Valour Credits.
            active.Active = false;
            active.PendingType = null;
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
            PendingType = state.PendingTier?.Name,
            AppStoreOriginalTransactionId = state.OriginalTransactionId,
            AppStoreTransactionId = state.TransactionId,
            AppStoreExpiry = state.Expiry,
        };

        _db.UserSubscriptions.Add(sub);
        user.SubscriptionType = state.Tier.Name;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The app's report and Apple's notification arrived together and
            // the other one recorded the purchase first.
            _db.ChangeTracker.Clear();
            var existing = await _db.UserSubscriptions
                .FirstOrDefaultAsync(x => x.AppStoreOriginalTransactionId == state.OriginalTransactionId);
            return existing is null
                ? TaskResult<Valour.Database.UserSubscription>.FromFailure("Failed to record the subscription.")
                : TaskResult<Valour.Database.UserSubscription>.FromData(existing);
        }

        if (state.EarnsWelcomeReward)
        {
            await StripeApi.DepositVcRewardAsync(userId, state.Tier.VcReward,
                BuildFingerprint("app_store_reward", state.TransactionId),
                $"App Store {state.Tier.Name} Subscription Reward - Welcome!",
                _ecoService, _db, _logger);
        }

        return TaskResult<Valour.Database.UserSubscription>.FromData(sub);
    }

    /// <summary>
    /// Re-reads active App Store subscriptions whose billing period has ended,
    /// in case a notification was missed.
    /// </summary>
    public async Task RefreshLapsedSubscriptionsAsync()
    {
        if (!IsConfigured)
            return;

        var cutoff = DateTime.UtcNow;
        var lapsed = await _db.UserSubscriptions
            .Where(x => x.Active && x.AppStoreOriginalTransactionId != null &&
                        (x.AppStoreExpiry == null || x.AppStoreExpiry < cutoff))
            .Select(x => new { x.Id, x.AppStoreOriginalTransactionId })
            .ToListAsync();

        foreach (var item in lapsed)
        {
            try
            {
                var result = await RefreshSubscriptionAsync(item.AppStoreOriginalTransactionId);
                if (result.Success)
                    continue;

                // Apple no longer knows the purchase, so it cannot renew.
                var sub = await _db.UserSubscriptions.FirstOrDefaultAsync(x => x.Id == item.Id);
                if (sub is { Active: true } && sub.AppStoreExpiry < cutoff.AddDays(-7))
                {
                    sub.Active = false;
                    var user = await _db.Users.FindAsync(sub.UserId);
                    if (user is not null)
                        user.SubscriptionType = await SubscriptionService.GetRemainingTypeAsync(_db, sub.UserId, sub.Id);
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to refresh an App Store subscription");
            }
        }
    }

    #endregion

    #region Valour Credits

    /// <summary>
    /// Verifies a Valour Credits purchase the app reports for the signed-in
    /// user and deposits the credits. The app finishes the StoreKit
    /// transaction only after this succeeds.
    /// </summary>
    public async Task<TaskResult> ClaimCreditsAsync(long userId, string productId, string transactionId)
    {
        if (!IsConfigured)
            return TaskResult.FromFailure("App Store billing is not available.");

        if (!AppStoreProducts.CreditPacks.ContainsKey(productId ?? ""))
            return TaskResult.FromFailure("Unknown product.");

        var transaction = await GetTransactionAsync(transactionId);
        if (transaction is null || transaction.ProductId != productId)
            return TaskResult.FromFailure("The App Store does not recognize this purchase.");

        if (AppStoreAccountToken.ToUserId(transaction.AppAccountToken) != userId)
            return TaskResult.FromFailure("This purchase belongs to a different account.");

        return await FulfillCreditsAsync(userId, transaction);
    }

    private async Task<TaskResult> FulfillCreditsAsync(long userId, AppStoreTransaction transaction)
    {
        if (transaction.RevocationDate is not null)
            return TaskResult.FromFailure("This purchase was refunded.");

        if (!AppStoreProducts.CreditPacks.TryGetValue(transaction.ProductId ?? "", out var credits))
            return TaskResult.FromFailure("Unknown product.");

        var fingerprint = BuildFingerprint("app_store_credits", transaction.TransactionId);
        if (await _db.Transactions.AnyAsync(x => x.Fingerprint == fingerprint))
            return TaskResult.SuccessResult;

        await StripeApi.DepositVcRewardAsync(userId, credits, fingerprint,
            "App Store Purchase - Thank you!", _ecoService, _db, _logger);

        // The fingerprint prevents a second deposit if the app reports the
        // purchase again before it finishes the transaction.
        return await _db.Transactions.AnyAsync(x => x.Fingerprint == fingerprint)
            ? TaskResult.SuccessResult
            : TaskResult.FromFailure("Failed to deposit your credits. They will be retried.");
    }

    #endregion

    #region Notifications

    /// <summary>
    /// Handles an App Store Server Notification (version 2). Its contents are
    /// only used to find the transaction, which is then read back from Apple,
    /// so a forged notification can do no more than trigger a refresh.
    /// </summary>
    public async Task HandleNotificationAsync(string signedPayload)
    {
        var notification = DecodePayload<AppStoreNotification>(signedPayload);
        if (notification is null)
        {
            _logger.LogWarning("Ignoring an App Store notification that could not be read");
            return;
        }

        if (notification.NotificationType == "TEST")
        {
            _logger.LogInformation("Received App Store test notification");
            return;
        }

        if (notification.Data?.BundleId is not null && notification.Data.BundleId != BundleId)
        {
            _logger.LogWarning("Ignoring App Store notification for bundle {BundleId}", notification.Data.BundleId);
            return;
        }

        var transaction = DecodePayload<AppStoreTransaction>(notification.Data?.SignedTransactionInfo);
        if (transaction?.TransactionId is null)
            return;

        if (UserSubscriptionTypes.FromAppStoreProductId(transaction.ProductId) is not null)
        {
            await RefreshSubscriptionAsync(transaction.TransactionId);
            return;
        }

        if (!AppStoreProducts.CreditPacks.ContainsKey(transaction.ProductId ?? ""))
            return;

        if (notification.NotificationType == "REFUND")
        {
            _logger.LogWarning("The App Store refunded a Valour Credits purchase (transaction {TransactionId})",
                transaction.TransactionId);
            return;
        }

        // A credit purchase the app did not get to report. Read it back from
        // Apple before depositing anything.
        var verified = await GetTransactionAsync(transaction.TransactionId);
        var userId = AppStoreAccountToken.ToUserId(verified?.AppAccountToken);
        if (verified is null || userId is null)
            return;

        await FulfillCreditsAsync(userId.Value, verified);
    }

    #endregion

    #region App Store Server API

    private async Task<AppStoreTransaction> GetTransactionAsync(string transactionId)
    {
        var response = await GetAsync<SignedTransactionResponse>($"/inApps/v1/transactions/{Uri.EscapeDataString(transactionId ?? "")}");
        var transaction = DecodePayload<AppStoreTransaction>(response?.SignedTransactionInfo);
        return transaction?.BundleId == BundleId ? transaction : null;
    }

    /// <summary>
    /// Reads the current status of the subscription that a transaction
    /// belongs to, including its latest transaction and renewal settings.
    /// </summary>
    private async Task<AppStoreSubscriptionStatus> GetSubscriptionStatusAsync(string transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            return null;

        var response = await GetAsync<SubscriptionStatusesResponse>($"/inApps/v1/subscriptions/{Uri.EscapeDataString(transactionId)}");
        if (response?.Data is null || response.BundleId != BundleId)
            return null;

        // Every subscription in the group shares this lookup. The original
        // transaction ID ties the requested one to its entry.
        var requested = await GetTransactionAsync(transactionId);
        if (requested is null)
            return null;

        foreach (var group in response.Data)
        {
            foreach (var item in group.LastTransactions ?? [])
            {
                if (item.OriginalTransactionId != requested.OriginalTransactionId)
                    continue;

                return new AppStoreSubscriptionStatus(
                    item.Status,
                    DecodePayload<AppStoreTransaction>(item.SignedTransactionInfo),
                    DecodePayload<AppStoreRenewalInfo>(item.SignedRenewalInfo));
            }
        }

        return null;
    }

    /// <summary>
    /// Calls the App Store Server API. Purchases made by App Review and
    /// TestFlight testers live in the sandbox, so a purchase the production
    /// environment doesn't know is looked up there.
    /// </summary>
    private async Task<T> GetAsync<T>(string path) where T : class
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);
        var token = CreateApiToken();

        foreach (var baseUrl in new[] { ProductionUrl, SandboxUrl })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
                continue;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("App Store Server API {Path} returned {Status}", path, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        }

        return null;
    }

    /// <summary>
    /// The bearer token the App Store Server API expects: ES256, signed with
    /// the In-App Purchase key, valid for at most an hour.
    /// </summary>
    private static string CreateApiToken()
    {
        var config = AppStoreConfig.Current!;
        var pem = !string.IsNullOrWhiteSpace(config.PrivateKey)
            ? config.PrivateKey
            : File.ReadAllText(config.PrivateKeyPath!);

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = config.IssuerId,
            Audience = "appstoreconnect-v1",
            IssuedAt = now,
            Expires = now.AddMinutes(20),
            Claims = new Dictionary<string, object> { ["bid"] = config.BundleId! },
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(ecdsa) { KeyId = config.KeyId }, SecurityAlgorithms.EcdsaSha256),
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>
    /// Reads the payload of a JWS from Apple. Payloads from the App Store
    /// Server API arrive over TLS in answer to our signed request, so they are
    /// trusted as they are; notification payloads are only used to look a
    /// transaction up again.
    /// </summary>
    internal static T DecodePayload<T>(string jws) where T : class
    {
        if (string.IsNullOrEmpty(jws))
            return null;

        var parts = jws.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(Base64UrlEncoder.DecodeBytes(parts[1]), JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    #endregion

    private static string BuildFingerprint(string prefix, string identifier)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier))).ToLowerInvariant();
        return $"{prefix}:{hash[..24]}";
    }

    private sealed class SignedTransactionResponse
    {
        public string SignedTransactionInfo { get; set; }
    }

    private sealed class SubscriptionStatusesResponse
    {
        public string BundleId { get; set; }
        public List<SubscriptionGroupStatus> Data { get; set; }
    }

    private sealed class SubscriptionGroupStatus
    {
        public List<LastTransactionItem> LastTransactions { get; set; }
    }

    private sealed class LastTransactionItem
    {
        public string OriginalTransactionId { get; set; }
        public int Status { get; set; }
        public string SignedTransactionInfo { get; set; }
        public string SignedRenewalInfo { get; set; }
    }

    private sealed class AppStoreNotification
    {
        public string NotificationType { get; set; }
        public AppStoreNotificationData Data { get; set; }
    }

    private sealed class AppStoreNotificationData
    {
        public string BundleId { get; set; }
        public string SignedTransactionInfo { get; set; }
    }
}

/// <summary>A decoded App Store transaction (JWSTransactionDecodedPayload).</summary>
public sealed class AppStoreTransaction
{
    public string TransactionId { get; set; }
    public string OriginalTransactionId { get; set; }
    public string BundleId { get; set; }
    public string ProductId { get; set; }
    public string AppAccountToken { get; set; }

    /// <summary>"PURCHASE" for a purchase or plan change, "RENEWAL" for an automatic renewal.</summary>
    public string TransactionReason { get; set; }

    /// <summary>Milliseconds since the Unix epoch.</summary>
    public long? ExpiresDate { get; set; }

    /// <summary>Set when Apple refunded or revoked the transaction.</summary>
    public long? RevocationDate { get; set; }
}

/// <summary>A decoded App Store renewal info payload (JWSRenewalInfoDecodedPayload).</summary>
public sealed class AppStoreRenewalInfo
{
    /// <summary>1 when the subscription renews, 0 when the person turned renewal off.</summary>
    public int AutoRenewStatus { get; set; }

    /// <summary>The product the subscription renews into, which differs after a downgrade.</summary>
    public string AutoRenewProductId { get; set; }

    /// <summary>Milliseconds since the Unix epoch, set while in a billing grace period.</summary>
    public long? GracePeriodExpiresDate { get; set; }
}

/// <summary>One entry of the App Store subscription status response.</summary>
public sealed record AppStoreSubscriptionStatus(int Status, AppStoreTransaction Transaction, AppStoreRenewalInfo Renewal);

/// <summary>
/// What an App Store subscription means for Valour at a given time.
/// </summary>
public sealed record AppStoreSubscriptionState(
    UserSubscriptionType Tier,
    UserSubscriptionType PendingTier,
    bool Entitled,
    bool PaymentFailed,
    bool AutoRenewing,
    string TransactionId,
    string OriginalTransactionId,
    bool IsRenewalTransaction,
    DateTime? Expiry,
    long? AccountUserId)
{
    // Subscription status values from the App Store Server API.
    public const int StatusActive = 1;
    public const int StatusExpired = 2;
    public const int StatusBillingRetry = 3;
    public const int StatusGracePeriod = 4;
    public const int StatusRevoked = 5;

    /// <summary>
    /// Reads a subscription status. Returns null when the product is not a
    /// Stargazer tier.
    /// </summary>
    public static AppStoreSubscriptionState Read(AppStoreSubscriptionStatus status, DateTime now)
    {
        var transaction = status?.Transaction;
        var tier = UserSubscriptionTypes.FromAppStoreProductId(transaction?.ProductId);
        if (transaction?.OriginalTransactionId is null || tier is null)
            return null;

        var expiry = FromMilliseconds(transaction.ExpiresDate);
        var graceExpiry = FromMilliseconds(status.Renewal?.GracePeriodExpiresDate);

        // An active subscription is usable until its period ends, including
        // one whose renewal is turned off. During a billing grace period it
        // stays usable while Apple retries the payment.
        var entitled = transaction.RevocationDate is null &&
                       (status.Status == StatusActive && expiry > now ||
                        status.Status == StatusGracePeriod && (graceExpiry ?? expiry) > now);

        var renewalProduct = status.Renewal?.AutoRenewProductId;
        var pendingTier = renewalProduct is not null && renewalProduct != transaction.ProductId
            ? UserSubscriptionTypes.FromAppStoreProductId(renewalProduct)
            : null;

        return new AppStoreSubscriptionState(
            tier,
            pendingTier,
            entitled,
            status.Status is StatusBillingRetry or StatusGracePeriod,
            status.Renewal?.AutoRenewStatus == 1,
            transaction.TransactionId,
            transaction.OriginalTransactionId,
            transaction.TransactionReason == "RENEWAL",
            expiry,
            AppStoreAccountToken.ToUserId(transaction.AppAccountToken));
    }

    /// <summary>
    /// Each renewal is a new transaction with the reason RENEWAL. A plan change
    /// is also a new transaction but a PURCHASE, so it earns no reward.
    /// </summary>
    public bool IsRenewalOf(string recordedTransactionId) =>
        Entitled && IsRenewalTransaction && TransactionId is not null && TransactionId != recordedTransactionId;

    /// <summary>
    /// Only the first purchase in the subscription group earns the welcome
    /// reward, so resubscribing or switching plans cannot collect it again.
    /// </summary>
    public bool EarnsWelcomeReward =>
        TransactionId is not null && TransactionId == OriginalTransactionId && !IsRenewalTransaction;

    private static DateTime? FromMilliseconds(long? value) =>
        value is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(value.Value).UtcDateTime;
}
