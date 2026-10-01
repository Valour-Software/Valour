# Payments and subscriptions

Valour sells two things: Stargazer subscriptions and Valour Credits (VC). This
guide explains how each one can be paid for, how the server keeps
subscriptions in sync with the payment provider, and how to set up Google Play
Billing for the Android app.

## Ways to pay

A Stargazer subscription is a row in `user_subscriptions`
([UserSubscription.cs](../Valour/Database/UserSubscription.cs)). The user's
current tier is also copied to `users.subscription_type`, which is what perks
such as the upload limit read. A user has at most one active subscription. It
is billed in one of three ways, and the columns that are set show which:

| Billing | Set column | Renewal is driven by |
| --- | --- | --- |
| Valour Credits | Neither | `SubscriptionService.ProcessActiveDue`, run hourly by `SubscriptionWorker` |
| Stripe (card) | `stripe_subscription_id` | Stripe webhooks to `api/stripe/webhook` |
| Google Play | `google_play_purchase_token` | Google Play notifications to `api/google-play/notifications` |

The tiers and their prices are defined in
[ISharedUserSubscription.cs](../Valour/Shared/Models/ISharedUserSubscription.cs).
Stripe and Google Play subscribers receive the tier's `VcReward` in credits
with each paid month.

Valour Credits can be bought as one-time packs through Stripe checkout
([StripeApi.cs](../Valour/Server/Api/Dynamic/StripeApi.cs)) or Google Play
([GooglePlayProducts](../Valour/Shared/Models/GooglePlayPurchase.cs)).

## Which checkout the client shows

Google Play requires apps it distributes to sell digital goods through Google
Play Billing. The Android app built with `ValourPlayStore=true` registers
[`IStoreBillingService`](../Valour/Client/Device/IStoreBillingService.cs), and
the subscriptions page
([EditSubscriptionsComponent.razor](../Valour/Client/Components/Menus/Modals/Users/Edit/EditSubscriptionsComponent.razor))
then sells through Google Play and hides every Stripe purchase. All other
builds, including the Android APK published on GitHub, use Stripe.

A subscription can only be changed where its billing lives. A Google Play
subscription can be cancelled from any client, because the server can stop its
renewal, but plan changes require the Play Store build. A card subscription
cannot be changed from the Play Store build.

## Google Play purchases

[`GooglePlayStoreBillingService`](../Valour/Client.Maui/Platforms/Android/GooglePlayStoreBillingService.cs)
runs checkout with the Play Billing Library. It sets the purchase's obfuscated
account ID to the Valour user ID, then reports the purchase token to the server:

- `POST api/google-play/subscriptions` for a subscription
- `POST api/google-play/credits` for a credit pack

[`GooglePlayBillingService`](../Valour/Server/Services/GooglePlayBillingService.cs)
never trusts the client's report. It reads the purchase back from the Google
Play Developer API, checks that the obfuscated account ID matches the signed-in
user, and records it. Subscriptions are then acknowledged and credit packs are
consumed. Google refunds a purchase that is not acknowledged within three days,
and a consumed pack can be bought again.

The app reports purchases again whenever the subscriptions page opens, in case
it closed during checkout. Google also sends real-time developer notifications
through Pub/Sub to `api/google-play/notifications`. A notification only names a
purchase, and the server reads the purchase from Google before acting. Because
the account ID travels with the purchase, a notification alone is enough to
record a purchase the app never reported.

### Subscription state

`GooglePlaySubscriptionState` turns a Google Play purchase into what Valour
needs to know:

- Active, in grace period, and canceled subscriptions keep their perks until
  the paid period ends. A canceled subscription has stopped renewing but has
  not expired yet.
- A grace period or account hold sets `stripe_payment_failed`, which the client
  shows as a failed payment for either provider.
- Each successful charge has its own order ID. A new order ID on a known
  purchase is a renewal, which updates `last_charged` and deposits the monthly
  reward. The reward's transaction fingerprint comes from the order ID, so a
  repeated notification cannot pay it twice.
- A plan change in Google Play issues a new purchase token whose
  `linkedPurchaseToken` names the old one. The server ends the old
  subscription and starts a new one. Plan changes do not earn the welcome
  reward, so switching plans back and forth cannot collect it repeatedly.
- When an account hold ends, Google Play reuses the same token and the
  subscription resumes.

`SubscriptionWorker` also re-reads active Google Play subscriptions whose
recorded billing period has ended, in case a notification was missed.

Refunds of credit packs are logged but do not remove credits, because the
credits may already have been spent.

Deleting an account stops renewal of its Google Play subscriptions before any
data is removed. If Google Play cannot be reached, the account is not deleted.

## Setting up Google Play Billing

The server needs a service account that can read and manage purchases:

1. In Google Cloud, in the project linked to the Play developer account, enable
   the Google Play Android Developer API and create a service account with a
   JSON key.
2. In the Play Console, under Users and permissions, invite the service
   account's email to the app with the "View financial data" and "Manage orders
   and subscriptions" permissions. Google can take up to a day to recognize a
   newly invited account.
3. Create a Pub/Sub topic and grant
   `google-play-developer-notifications@system.gserviceaccount.com` the Pub/Sub
   Publisher role on it. Add a push subscription to the topic whose endpoint is
   `https://<api host>/api/google-play/notifications?token=<secret>`.
4. In the Play Console, under Monetize with Play, then Monetization setup, enter
   the topic name and send a test notification. The server logs
   "Received Google Play test notification".

Then add a `GooglePlay` section to the server's `appsettings.json`
([GooglePlayConfig.cs](../Config/Configs/GooglePlayConfig.cs)):

```json
"GooglePlay": {
  "PackageName": "gg.valour.app",
  "ServiceAccountPath": "/path/to/play-service-account.json",
  "NotificationToken": "<the same secret as the push endpoint>"
}
```

The products in the Play Console must use these IDs, because the server and
app look them up by ID:

| Product | Type | ID |
| --- | --- | --- |
| Stargazer | Subscription, one monthly auto-renewing base plan | `stargazer` |
| Stargazer Plus | Subscription, one monthly auto-renewing base plan | `stargazer_plus` |
| Stargazer Pro | Subscription, one monthly auto-renewing base plan | `stargazer_pro` |
| 500 Valour Credits | Consumable one-time product | `vc_500` |
| 1000 Valour Credits | Consumable one-time product | `vc_1000` |
| 2000 Valour Credits | Consumable one-time product | `vc_2000` |

Google Play only offers products to an app it installed, so test purchases
need a build from a Play testing track and a license tester account.
