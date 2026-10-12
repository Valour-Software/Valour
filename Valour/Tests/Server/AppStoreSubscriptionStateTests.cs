using System.Text;
using System.Text.Json;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Server;

public class AppStoreSubscriptionStateTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private const long UserId = 123456789;

    private static long Ms(DateTime time) => new DateTimeOffset(time).ToUnixTimeMilliseconds();

    private static AppStoreSubscriptionStatus Status(
        int status = AppStoreSubscriptionState.StatusActive,
        string productId = "stargazer_plus",
        DateTime? expiry = null,
        int autoRenew = 1,
        string renewProductId = null,
        string transactionId = "2000000111",
        string originalTransactionId = "2000000111",
        string reason = "PURCHASE",
        string accountToken = null,
        DateTime? graceExpiry = null,
        DateTime? revoked = null) => new(
        status,
        new AppStoreTransaction
        {
            TransactionId = transactionId,
            OriginalTransactionId = originalTransactionId,
            BundleId = "gg.valour.app",
            ProductId = productId,
            AppAccountToken = accountToken ?? AppStoreAccountToken.FromUserId(UserId).ToString(),
            TransactionReason = reason,
            ExpiresDate = Ms(expiry ?? Now.AddDays(20)),
            RevocationDate = revoked is null ? null : Ms(revoked.Value),
        },
        new AppStoreRenewalInfo
        {
            AutoRenewStatus = autoRenew,
            AutoRenewProductId = renewProductId ?? productId,
            GracePeriodExpiresDate = graceExpiry is null ? null : Ms(graceExpiry.Value),
        });

    [Fact]
    public void AccountToken_RoundTripsUserId()
    {
        var token = AppStoreAccountToken.FromUserId(UserId).ToString();

        Assert.Equal(UserId, AppStoreAccountToken.ToUserId(token));
        Assert.Equal(UserId, AppStoreAccountToken.ToUserId(token.ToUpperInvariant()));
    }

    [Fact]
    public void AccountToken_RejectsOtherUuids()
    {
        Assert.Null(AppStoreAccountToken.ToUserId(Guid.NewGuid().ToString()));
        Assert.Null(AppStoreAccountToken.ToUserId("not-a-uuid"));
        Assert.Null(AppStoreAccountToken.ToUserId(null));
    }

    [Fact]
    public void Read_MapsProductToTierAndAccount()
    {
        var state = AppStoreSubscriptionState.Read(Status(), Now);

        Assert.NotNull(state);
        Assert.Same(UserSubscriptionTypes.StargazerPlus, state.Tier);
        Assert.Equal(UserId, state.AccountUserId);
        Assert.True(state.Entitled);
        Assert.True(state.AutoRenewing);
        Assert.False(state.PaymentFailed);
        Assert.Null(state.PendingTier);
    }

    [Fact]
    public void Read_ReturnsNullForUnknownProduct()
    {
        Assert.Null(AppStoreSubscriptionState.Read(Status(productId: "vc_500"), Now));
        Assert.Null(AppStoreSubscriptionState.Read(new AppStoreSubscriptionStatus(1, null, null), Now));
    }

    [Fact]
    public void Read_RenewalOffStaysEntitledUntilExpiry()
    {
        var state = AppStoreSubscriptionState.Read(Status(autoRenew: 0), Now);

        Assert.True(state.Entitled);
        Assert.False(state.AutoRenewing);
    }

    [Fact]
    public void Read_GracePeriodIsEntitledButFlagsPayment()
    {
        var state = AppStoreSubscriptionState.Read(Status(
            status: AppStoreSubscriptionState.StatusGracePeriod,
            expiry: Now.AddDays(-1),
            graceExpiry: Now.AddDays(5)), Now);

        Assert.True(state.Entitled);
        Assert.True(state.PaymentFailed);
    }

    [Theory]
    [InlineData(AppStoreSubscriptionState.StatusExpired)]
    [InlineData(AppStoreSubscriptionState.StatusBillingRetry)]
    [InlineData(AppStoreSubscriptionState.StatusRevoked)]
    public void Read_NonPayingStatesAreNotEntitled(int status)
    {
        Assert.False(AppStoreSubscriptionState.Read(Status(status: status), Now).Entitled);
    }

    [Fact]
    public void Read_RefundedTransactionIsNotEntitled()
    {
        Assert.False(AppStoreSubscriptionState.Read(Status(revoked: Now.AddDays(-1)), Now).Entitled);
    }

    [Fact]
    public void Read_ScheduledDowngradeIsPending()
    {
        var state = AppStoreSubscriptionState.Read(Status(productId: "stargazer_pro", renewProductId: "stargazer"), Now);

        Assert.Same(UserSubscriptionTypes.StargazerPro, state.Tier);
        Assert.Same(UserSubscriptionTypes.Stargazer, state.PendingTier);
    }

    [Fact]
    public void Read_ForeignAccountTokenHasNoUser()
    {
        Assert.Null(AppStoreSubscriptionState.Read(Status(accountToken: Guid.NewGuid().ToString()), Now).AccountUserId);
    }

    [Fact]
    public void IsRenewalOf_OnlyForNewRenewalTransactions()
    {
        var renewal = AppStoreSubscriptionState.Read(Status(transactionId: "2000000222", reason: "RENEWAL"), Now);
        Assert.True(renewal.IsRenewalOf("2000000111"));
        Assert.False(renewal.IsRenewalOf("2000000222"));

        // An upgrade is a new transaction, but a purchase rather than a renewal.
        var upgrade = AppStoreSubscriptionState.Read(Status(transactionId: "2000000333", productId: "stargazer_pro"), Now);
        Assert.False(upgrade.IsRenewalOf("2000000111"));
    }

    [Fact]
    public void EarnsWelcomeReward_OnlyForFirstPurchaseInGroup()
    {
        Assert.True(AppStoreSubscriptionState.Read(Status(), Now).EarnsWelcomeReward);
        Assert.False(AppStoreSubscriptionState.Read(Status(transactionId: "2000000444"), Now).EarnsWelcomeReward);
    }

    [Fact]
    public void EveryTierHasADistinctAppStoreProduct()
    {
        var ids = UserSubscriptionTypes.TypeMap.Values.Select(x => x.AppStoreProductId).ToList();

        Assert.All(ids, Assert.NotNull);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Empty(ids.Intersect(AppStoreProducts.CreditPacks.Keys));
    }

    [Fact]
    public void DecodePayload_ReadsJwsPayload()
    {
        static string Part(object value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var jws = $"{Part(new { alg = "ES256" })}.{Part(new { transactionId = "42", productId = "vc_500", transactionReason = "PURCHASE" })}.c2ln";
        var transaction = AppStoreBillingService.DecodePayload<AppStoreTransaction>(jws);

        Assert.Equal("42", transaction.TransactionId);
        Assert.Equal("vc_500", transaction.ProductId);
        Assert.Null(AppStoreBillingService.DecodePayload<AppStoreTransaction>("not a jws"));
    }
}
