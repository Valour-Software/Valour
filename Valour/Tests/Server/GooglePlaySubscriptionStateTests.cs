using System.Text.Json;
using Google.Apis.AndroidPublisher.v3.Data;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Server;

public class GooglePlaySubscriptionStateTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SubscriptionPurchaseV2 Purchase(
        string state = "SUBSCRIPTION_STATE_ACTIVE",
        string productId = "stargazer_plus",
        DateTime? expiry = null,
        bool autoRenew = true,
        string orderId = "GPA.1111-2222-3333-44444",
        string linkedToken = null,
        string accountId = "123456789",
        string acknowledgement = "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED") => new()
    {
        SubscriptionState = state,
        LinkedPurchaseToken = linkedToken,
        AcknowledgementState = acknowledgement,
        ExternalAccountIdentifiers = new ExternalAccountIdentifiers { ObfuscatedExternalAccountId = accountId },
        LineItems =
        [
            new SubscriptionPurchaseLineItem
            {
                ProductId = productId,
                ExpiryTimeDateTimeOffset = expiry ?? Now.AddDays(20),
                LatestSuccessfulOrderId = orderId,
                AutoRenewingPlan = new AutoRenewingPlan { AutoRenewEnabled = autoRenew },
            }
        ],
    };

    [Fact]
    public void Read_MapsProductToTierAndAccount()
    {
        var state = GooglePlaySubscriptionState.Read(Purchase(), Now);

        Assert.NotNull(state);
        Assert.Same(UserSubscriptionTypes.StargazerPlus, state.Tier);
        Assert.Equal(123456789, state.AccountUserId);
        Assert.True(state.Entitled);
        Assert.True(state.AutoRenewing);
        Assert.False(state.PaymentFailed);
    }

    [Fact]
    public void Read_ReturnsNullForUnknownProduct()
    {
        Assert.Null(GooglePlaySubscriptionState.Read(Purchase(productId: "vc_500"), Now));
        Assert.Null(GooglePlaySubscriptionState.Read(new SubscriptionPurchaseV2(), Now));
    }

    [Fact]
    public void Read_CanceledStaysEntitledUntilExpiry()
    {
        var canceled = GooglePlaySubscriptionState.Read(
            Purchase(state: "SUBSCRIPTION_STATE_CANCELED", autoRenew: false), Now);
        Assert.True(canceled.Entitled);
        Assert.False(canceled.AutoRenewing);

        var lapsed = GooglePlaySubscriptionState.Read(
            Purchase(state: "SUBSCRIPTION_STATE_CANCELED", autoRenew: false, expiry: Now.AddMinutes(-1)), Now);
        Assert.False(lapsed.Entitled);
    }

    [Fact]
    public void Read_GracePeriodIsEntitledButFlagsPayment()
    {
        var state = GooglePlaySubscriptionState.Read(Purchase(state: "SUBSCRIPTION_STATE_IN_GRACE_PERIOD"), Now);

        Assert.True(state.Entitled);
        Assert.True(state.PaymentFailed);
    }

    [Theory]
    [InlineData("SUBSCRIPTION_STATE_ON_HOLD", true)]
    [InlineData("SUBSCRIPTION_STATE_PAUSED", false)]
    [InlineData("SUBSCRIPTION_STATE_EXPIRED", false)]
    [InlineData("SUBSCRIPTION_STATE_PENDING", false)]
    [InlineData("SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED", false)]
    public void Read_NonPayingStatesAreNotEntitled(string subscriptionState, bool paymentFailed)
    {
        var state = GooglePlaySubscriptionState.Read(Purchase(state: subscriptionState), Now);

        Assert.False(state.Entitled);
        Assert.Equal(paymentFailed, state.PaymentFailed);
    }

    [Fact]
    public void Read_AccountIdMustBeNumeric()
    {
        Assert.Null(GooglePlaySubscriptionState.Read(Purchase(accountId: null), Now).AccountUserId);
        Assert.Null(GooglePlaySubscriptionState.Read(Purchase(accountId: "abc"), Now).AccountUserId);
    }

    [Fact]
    public void Read_FlagsUnacknowledgedPurchases()
    {
        var state = GooglePlaySubscriptionState.Read(Purchase(acknowledgement: "ACKNOWLEDGEMENT_STATE_PENDING"), Now);

        Assert.True(state.NeedsAcknowledgement);
    }

    [Fact]
    public void IsRenewalOf_OnlyForNewOrdersWhileEntitled()
    {
        var state = GooglePlaySubscriptionState.Read(Purchase(orderId: "GPA.1111-2222-3333-44444..0"), Now);

        Assert.True(state.IsRenewalOf("GPA.1111-2222-3333-44444"));
        Assert.False(state.IsRenewalOf("GPA.1111-2222-3333-44444..0"));

        var expired = GooglePlaySubscriptionState.Read(
            Purchase(state: "SUBSCRIPTION_STATE_EXPIRED", orderId: "GPA.1111-2222-3333-44444..1"), Now);
        Assert.False(expired.IsRenewalOf("GPA.1111-2222-3333-44444..0"));
    }

    [Fact]
    public void EarnsWelcomeReward_NotForPlanChanges()
    {
        Assert.True(GooglePlaySubscriptionState.Read(Purchase(), Now).EarnsWelcomeReward);
        Assert.False(GooglePlaySubscriptionState.Read(Purchase(linkedToken: "old-token"), Now).EarnsWelcomeReward);
        Assert.False(GooglePlaySubscriptionState.Read(Purchase(orderId: null), Now).EarnsWelcomeReward);
    }

    [Fact]
    public void EveryTierHasADistinctGooglePlayProduct()
    {
        var productIds = UserSubscriptionTypes.TypeMap.Values.Select(x => x.GooglePlayProductId).ToList();

        Assert.All(productIds, Assert.NotNull);
        Assert.Equal(productIds.Count, productIds.Distinct().Count());
        Assert.All(UserSubscriptionTypes.TypeMap.Values,
            tier => Assert.Same(tier, UserSubscriptionTypes.FromGooglePlayProductId(tier.GooglePlayProductId)));
    }

    [Fact]
    public void Notification_ParsesDeveloperNotificationPayload()
    {
        const string json = """
            {
              "version": "1.0",
              "packageName": "gg.valour.app",
              "eventTimeMillis": "1759320000000",
              "subscriptionNotification": { "version": "1.0", "notificationType": 2, "purchaseToken": "sub-token", "subscriptionId": "stargazer" },
              "voidedPurchaseNotification": { "purchaseToken": "void-token", "orderId": "GPA.1", "productType": 1, "refundType": 1 }
            }
            """;

        var notification = JsonSerializer.Deserialize<GooglePlayNotification>(json);

        Assert.Equal("gg.valour.app", notification.PackageName);
        Assert.Equal(2, notification.SubscriptionNotification.NotificationType);
        Assert.Equal("sub-token", notification.SubscriptionNotification.PurchaseToken);
        Assert.Equal(1, notification.VoidedPurchaseNotification.ProductType);
        Assert.Null(notification.OneTimeProductNotification);
    }
}
