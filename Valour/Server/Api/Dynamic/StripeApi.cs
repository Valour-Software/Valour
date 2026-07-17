using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Stripe;
using Stripe.Checkout;
using Valour.Config.Configs;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class StripeApi
{
    private static readonly Dictionary<string, (string Name, long UnitAmount, int CreditAmount)> Products = new()
    {
        ["VC500"] = ("500 Valour Credits", 500, 500),     // $5.00
        ["VC1000"] = ("1000 Valour Credits", 950, 1000),  // $9.50
        ["VC2000"] = ("2000 Valour Credits", 1800, 2000), // $18.00
    };

    /// <summary>
    /// Cache for Stripe Price IDs keyed by lookup key
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _stripePriceCache = new();

    [ValourRoute(HttpVerbs.Post, "api/stripe/checkout/{productId}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> CreateCheckoutAsync(
        UserService userService,
        string productId,
        ILogger<StripeApi> logger)
    {
        if (!Products.TryGetValue(productId, out var product))
            return ValourResult.BadRequest("Unknown product id " + productId);

        var userId = await userService.GetCurrentUserIdAsync();

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = product.UnitAmount,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = product.Name,
                            Description = $"{product.Name}, for use on {ValourHosts.RootDomain}",
                        }
                    },
                    Quantity = 1,
                }
            },
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId.ToString(),
                ["productId"] = productId,
                ["creditAmount"] = product.CreditAmount.ToString(),
            },
            SuccessUrl = StripeConfig.Current.SuccessUrl,
            CancelUrl = StripeConfig.Current.CancelUrl,
        };

        try
        {
            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return Results.Json(new { sessionUrl = session.Url });
        }
        catch (StripeException ex)
        {
            logger.LogError(ex,
                "Stripe checkout session creation failed for user {UserId}, product {ProductId}",
                userId, productId);
            return Results.Problem("Failed to create checkout session.", statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected checkout session creation failure for user {UserId}, product {ProductId}",
                userId, productId);
            return Results.Problem("Failed to create checkout session.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [ValourRoute(HttpVerbs.Post, "api/stripe/subscribe/{subType}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> CreateSubscriptionCheckoutAsync(
        UserService userService, string subType, ValourDb db, ILogger<StripeApi> logger)
    {
        if (!UserSubscriptionTypes.TypeMap.TryGetValue(subType, out var subTypeObj))
            return ValourResult.BadRequest("Unknown subscription type: " + subType);

        var normalizedSubType = subTypeObj.Name;
        var userId = await userService.GetCurrentUserIdAsync();

        // Check for existing active subscription.
        // Allow VC-managed subscriptions to migrate to Stripe,
        // but prevent creating a second Stripe subscription.
        var hasActiveStripeSub = await db.UserSubscriptions
            .AnyAsync(x => x.Active && x.UserId == userId && x.StripeSubscriptionId != null);
        if (hasActiveStripeSub)
            return ValourResult.BadRequest("You already have an active Stripe subscription. Use the plan change flow instead.");

        var existingSub = await db.UserSubscriptions
            .Where(x => x.Active && x.UserId == userId && x.StripeSubscriptionId == null)
            .OrderByDescending(x => x.LastCharged)
            .FirstOrDefaultAsync();

        long migrationCreditCents = 0;
        if (existingSub is not null &&
            UserSubscriptionTypes.TypeMap.TryGetValue(existingSub.Type, out var currentTypeObj))
        {
            migrationCreditCents = CalculateVcToStripeProrationCreditCents(existingSub, currentTypeObj);
            migrationCreditCents = Math.Min(migrationCreditCents, subTypeObj.StripePriceCents);
        }

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = subTypeObj.StripePriceCents,
                        Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = "month",
                        },
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Valour {subTypeObj.Name} Subscription",
                            Description = $"Monthly {subTypeObj.Name} subscription on {ValourHosts.RootDomain}",
                        }
                    },
                    Quantity = 1,
                }
            },
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId.ToString(),
                ["subType"] = normalizedSubType,
            },
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["userId"] = userId.ToString(),
                    ["subType"] = normalizedSubType,
                },
            },
            SuccessUrl = StripeConfig.Current.SubscriptionSuccessUrl,
            CancelUrl = StripeConfig.Current.SubscriptionCancelUrl,
        };

        // Apply one-time credit to the first Stripe invoice when migrating from an active VC subscription.
        if (migrationCreditCents > 0)
        {
            try
            {
                var couponService = new CouponService();
                var coupon = await couponService.CreateAsync(new CouponCreateOptions
                {
                    AmountOff = migrationCreditCents,
                    Currency = "usd",
                    Duration = "once",
                    Name = $"VC migration credit ({migrationCreditCents} cents)",
                    MaxRedemptions = 1,
                });

                options.Discounts = new List<SessionDiscountOptions>
                {
                    new() { Coupon = coupon.Id }
                };

                options.Metadata["vcMigrationCreditCents"] = migrationCreditCents.ToString();
                options.SubscriptionData.Metadata["vcMigrationCreditCents"] = migrationCreditCents.ToString();
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to create VC migration credit coupon for user {UserId}", userId);
                return ValourResult.BadRequest("Failed to apply migration credit. Please try again.");
            }
        }

        try
        {
            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return Results.Json(new { sessionUrl = session.Url });
        }
        catch (StripeException ex)
        {
            logger.LogError(ex,
                "Stripe subscription checkout session creation failed for user {UserId}, tier {SubType}",
                userId, normalizedSubType);
            return Results.Problem("Failed to create subscription checkout session.", statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected subscription checkout session creation failure for user {UserId}, tier {SubType}",
                userId, normalizedSubType);
            return Results.Problem("Failed to create subscription checkout session.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [ValourRoute(HttpVerbs.Post, "api/stripe/subscriptions/cancel")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> CancelStripeSubscriptionAsync(
        UserService userService, ValourDb db, ILogger<StripeApi> logger)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        var activeSub = await db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.Active && x.UserId == userId);

        if (activeSub is null)
            return ValourResult.BadRequest("No active subscription found.");

        if (string.IsNullOrEmpty(activeSub.StripeSubscriptionId))
            return ValourResult.BadRequest("This subscription is not managed by Stripe. Use the regular cancel endpoint.");

        try
        {
            // Cancel at period end via Stripe
            var stripeService = new Stripe.SubscriptionService();
            await stripeService.UpdateAsync(activeSub.StripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true,
            });
        }
        catch (StripeException ex)
        {
            logger.LogError(ex,
                "Stripe subscription cancellation failed for user {UserId}", userId);
            return Results.Problem("Failed to cancel Stripe subscription.", statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected Stripe subscription cancellation failure for user {UserId}", userId);
            return Results.Problem("Failed to cancel Stripe subscription.", statusCode: StatusCodes.Status500InternalServerError);
        }

        activeSub.Cancelled = true;
        await db.SaveChangesAsync();

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Post, "api/stripe/subscriptions/change/{subType}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ChangeStripeSubscriptionAsync(
        UserService userService, string subType, ValourDb db, ILogger<StripeApi> logger)
    {
        if (!UserSubscriptionTypes.TypeMap.TryGetValue(subType, out var newTypeObj))
            return ValourResult.BadRequest("Unknown subscription type: " + subType);

        var normalizedNewType = newTypeObj.Name;
        var userId = await userService.GetCurrentUserIdAsync();

        var activeSub = await db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.Active && x.UserId == userId);

        if (activeSub is null)
            return ValourResult.BadRequest("No active subscription found.");

        if (string.IsNullOrEmpty(activeSub.StripeSubscriptionId))
            return ValourResult.BadRequest("This subscription is not managed by Stripe.");

        if (!UserSubscriptionTypes.TypeMap.TryGetValue(activeSub.Type, out var currentTypeObj))
            return ValourResult.BadRequest("Current subscription type is invalid.");

        if (currentTypeObj.Name == normalizedNewType)
            return ValourResult.BadRequest("You already have this subscription tier.");

        var user = await db.Users.FindAsync(userId);
        if (user is null)
            return ValourResult.BadRequest("User not found.");

        var isUpgrade = newTypeObj.StripePriceCents > currentTypeObj.StripePriceCents;

        try
        {
            // Get the Stripe Price ID for the new tier
            var newPriceId = await GetOrCreateStripePriceIdForTierAsync(newTypeObj);

            // Get the current Stripe subscription to find the subscription item ID
            var stripeService = new Stripe.SubscriptionService();
            var stripeSub = await stripeService.GetAsync(activeSub.StripeSubscriptionId);

            if (stripeSub.Items?.Data == null || stripeSub.Items.Data.Count == 0)
                return ValourResult.BadRequest("Could not find subscription items on Stripe.");

            var itemId = stripeSub.Items.Data[0].Id;

            // Update the Stripe subscription
            var updateOptions = new SubscriptionUpdateOptions
            {
                Items = new List<SubscriptionItemOptions>
                {
                    new()
                    {
                        Id = itemId,
                        Price = newPriceId,
                    }
                },
                ProrationBehavior = isUpgrade ? "create_prorations" : "none",
            };

            await stripeService.UpdateAsync(activeSub.StripeSubscriptionId, updateOptions);
        }
        catch (StripeException ex)
        {
            logger.LogError(ex,
                "Stripe subscription change failed for user {UserId}, new tier {SubType}",
                userId, normalizedNewType);
            return Results.Problem("Failed to change Stripe subscription.", statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected Stripe subscription change failure for user {UserId}, new tier {SubType}",
                userId, normalizedNewType);
            return Results.Problem("Failed to change Stripe subscription.", statusCode: StatusCodes.Status500InternalServerError);
        }

        // Update local DB
        if (isUpgrade)
        {
            // Immediate tier change for upgrades
            activeSub.Type = normalizedNewType;
            activeSub.PendingType = null;
            user.SubscriptionType = normalizedNewType;
        }
        else
        {
            // Schedule downgrade for next billing cycle
            activeSub.PendingType = normalizedNewType;
        }

        await db.SaveChangesAsync();

        var message = isUpgrade
            ? $"Upgraded to {normalizedNewType}! Prorated charges will appear on your next invoice."
            : $"Your plan will change to {normalizedNewType} at your next billing cycle.";

        return Results.Json(new { success = true, message });
    }

    private static long CalculateVcToStripeProrationCreditCents(
        Valour.Database.UserSubscription currentSub, UserSubscriptionType currentTypeObj)
    {
        var now = DateTime.UtcNow;
        var periodStart = currentSub.LastCharged;
        var periodEnd = periodStart.AddMonths(1);

        if (periodEnd <= now)
            return 0;

        var totalPeriodSeconds = (periodEnd - periodStart).TotalSeconds;
        if (totalPeriodSeconds <= 0)
            return 0;

        var remainingSeconds = (periodEnd - now).TotalSeconds;
        var remainingRatio = Math.Clamp(remainingSeconds / totalPeriodSeconds, 0, 1);
        return (long)Math.Round(currentTypeObj.StripePriceCents * remainingRatio, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Gets or creates a Stripe Price ID for a subscription tier using lookup keys.
    /// Public so SubscriptionService can use it to revert prices.
    /// </summary>
    public static async Task<string> GetOrCreateStripePriceIdForTierAsync(UserSubscriptionType tier)
    {
        var lookupSegment = tier.Name.ToLowerInvariant().Replace(' ', '_');
        var lookupKey = $"valour_{lookupSegment}_monthly";

        if (_stripePriceCache.TryGetValue(lookupKey, out var cachedPriceId))
            return cachedPriceId;

        // Search for existing price by lookup key
        var priceService = new PriceService();
        var prices = await priceService.ListAsync(new PriceListOptions
        {
            LookupKeys = new List<string> { lookupKey },
            Active = true,
        });

        if (prices.Data.Count > 0)
        {
            var priceId = prices.Data[0].Id;
            _stripePriceCache[lookupKey] = priceId;
            return priceId;
        }

        // Create a new price with the lookup key
        var createOptions = new PriceCreateOptions
        {
            Currency = "usd",
            UnitAmount = tier.StripePriceCents,
            Recurring = new PriceRecurringOptions
            {
                Interval = "month",
            },
            LookupKey = lookupKey,
            ProductData = new PriceProductDataOptions
            {
                Name = $"Valour {tier.Name} Subscription",
            },
        };

        var newPrice = await priceService.CreateAsync(createOptions);
        _stripePriceCache[lookupKey] = newPrice.Id;
        return newPrice.Id;
    }

    [ValourRoute(HttpVerbs.Post, "api/stripe/webhook")]
    public static async Task<IResult> WebhookAsync(
        HttpContext httpContext,
        EcoService ecoService,
        ValourDb db,
        ILogger<StripeApi> logger)
    {
        var json = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(StripeConfig.Current?.WebhookSecret))
        {
            logger.LogError("Stripe webhook rejected: missing Stripe webhook secret configuration.");
            return Results.Problem("Stripe webhook is not configured.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var sigHeader = httpContext.Request.Headers["Stripe-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(sigHeader))
        {
            logger.LogWarning("Stripe webhook rejected: missing Stripe-Signature header.");
            return Results.BadRequest("Missing Stripe-Signature header");
        }

        Event stripeEvent;
        try
        {
            // Do not reject events purely due to Stripe API date mismatch; verify signature and continue.
            stripeEvent = EventUtility.ConstructEvent(json, sigHeader, StripeConfig.Current.WebhookSecret, 300, false);
        }
        catch (StripeException ex)
        {
            string rawEventId = null;
            string rawEventType = null;

            try
            {
                var unverifiedEvent = EventUtility.ParseEvent(json, false);
                rawEventId = unverifiedEvent?.Id;
                rawEventType = unverifiedEvent?.Type;
            }
            catch
            {
                // Ignore parse failures when extracting debug context.
            }

            logger.LogWarning(ex,
                "Stripe webhook validation failed. EventId={EventId}, EventType={EventType}, ApiVersion={ApiVersion}",
                rawEventId ?? "<unknown>",
                rawEventType ?? "<unknown>",
                Stripe.StripeConfiguration.ApiVersion ?? "<default>");
            return Results.BadRequest("Invalid signature");
        }

        logger.LogInformation("Stripe webhook received: EventType={EventType}, EventId={EventId}, EventApiVersion={EventApiVersion}",
            stripeEvent.Type, stripeEvent.Id, stripeEvent.ApiVersion ?? "<unknown>");

        try
        {
            switch (stripeEvent.Type)
            {
                case EventTypes.CheckoutSessionCompleted:
                {
                    var session = stripeEvent.Data.Object as Session;
                    if (session is null) break;

                    if (session.Mode == "subscription")
                        await FulfillSubscriptionSessionAsync(session, ecoService, db, logger);
                    else
                        await FulfillSessionAsync(session, ecoService, db, logger);

                    break;
                }

                case EventTypes.InvoicePaid:
                {
                    var invoice = stripeEvent.Data.Object as Invoice;
                    if (invoice is null) break;

                    await HandleInvoicePaidAsync(invoice, ecoService, db, logger);
                    break;
                }

                case EventTypes.InvoicePaymentFailed:
                {
                    var invoice = stripeEvent.Data.Object as Invoice;
                    if (invoice is null) break;

                    await HandleInvoicePaymentFailedAsync(invoice, db, logger);
                    break;
                }

                case EventTypes.CustomerSubscriptionUpdated:
                {
                    var subscription = stripeEvent.Data.Object as Subscription;
                    if (subscription is null) break;

                    await HandleSubscriptionUpdatedAsync(subscription, db, logger);
                    break;
                }

                case EventTypes.CustomerSubscriptionDeleted:
                {
                    var subscription = stripeEvent.Data.Object as Subscription;
                    if (subscription is null) break;

                    await HandleSubscriptionDeletedAsync(subscription, db, logger);
                    break;
                }

                default:
                {
                    logger.LogDebug("Stripe webhook event ignored: EventType={EventType}, EventId={EventId}",
                        stripeEvent.Type, stripeEvent.Id);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stripe webhook fulfillment error: EventType={EventType}, EventId={EventId}",
                stripeEvent.Type, stripeEvent.Id);
            return Results.Problem("Webhook processing failed.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok();
    }

    /// <summary>
    /// Handles checkout.session.completed for subscription mode — initial subscription creation
    /// </summary>
    public static Task FulfillSubscriptionSessionAsync(Session session, EcoService ecoService, ValourDb db)
    {
        return FulfillSubscriptionSessionAsync(session, ecoService, db, null);
    }

    public static async Task FulfillSubscriptionSessionAsync(
        Session session,
        EcoService ecoService,
        ValourDb db,
        ILogger logger)
    {
        var metadata = session.Metadata;
        if (metadata is null ||
            !metadata.TryGetValue("userId", out var userIdStr) ||
            !metadata.TryGetValue("subType", out var subType))
        {
            logger?.LogWarning("Stripe subscription fulfillment skipped due to missing metadata.");
            return;
        }

        if (!long.TryParse(userIdStr, out var userId))
        {
            logger?.LogWarning("Stripe subscription fulfillment skipped due to invalid user ID metadata.");
            return;
        }

        if (!UserSubscriptionTypes.TypeMap.TryGetValue(subType, out var subTypeObj))
        {
            logger?.LogWarning("Stripe subscription fulfillment skipped due to unknown subscription type metadata. SubType={SubType}",
                subType);
            return;
        }

        var normalizedSubType = subTypeObj.Name;
        var stripeSubscriptionId = session.SubscriptionId;
        if (string.IsNullOrEmpty(stripeSubscriptionId))
        {
            logger?.LogWarning("Stripe subscription fulfillment skipped because subscription ID was missing.");
            return;
        }

        // Idempotency: skip if already exists
        var existing = await db.UserSubscriptions
            .AnyAsync(x => x.StripeSubscriptionId == stripeSubscriptionId);
        if (existing)
            return;

        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            logger?.LogWarning("Stripe subscription fulfillment skipped because user was not found. UserId={UserId}", userId);
            return;
        }

        // Migration path: deactivate active VC-managed subscriptions once Stripe subscription is confirmed.
        var activeVcSubs = await db.UserSubscriptions
            .Where(x => x.Active && x.UserId == userId && x.StripeSubscriptionId == null)
            .ToListAsync();

        foreach (var vcSub in activeVcSubs)
        {
            vcSub.Active = false;
            vcSub.PendingType = null;
        }

        var newSub = new Valour.Database.UserSubscription
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Type = normalizedSubType,
            Active = true,
            LastCharged = DateTime.UtcNow,
            Renewals = 0,
            Created = DateTime.UtcNow,
            StripeSubscriptionId = stripeSubscriptionId,
        };

        await db.UserSubscriptions.AddAsync(newSub);
        user.SubscriptionType = normalizedSubType;
        await db.SaveChangesAsync();

        var initialRewardFingerprint = BuildOpaqueStripeFingerprint("stripe_reward_initial", stripeSubscriptionId);

        // Deposit VC reward
        await DepositVcRewardAsync(userId, subTypeObj.VcReward,
            initialRewardFingerprint,
            $"Stripe {subTypeObj.Name} Subscription Reward - Welcome!",
            ecoService, db, logger);
    }

    /// <summary>
    /// Handles invoice.paid — monthly renewal billing
    /// </summary>
    private static async Task HandleInvoicePaidAsync(
        Invoice invoice,
        EcoService ecoService,
        ValourDb db,
        ILogger logger)
    {
        var stripeSubscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrEmpty(stripeSubscriptionId))
            return;

        // Skip the initial invoice — already handled by checkout.session.completed
        if (invoice.BillingReason == "subscription_create")
            return;

        var sub = await db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.StripeSubscriptionId == stripeSubscriptionId);
        if (sub is null)
            return;

        var rewardFingerprint = BuildOpaqueStripeFingerprint("stripe_reward_invoice", invoice.Id);

        // Handle pending tier change at renewal
        if (sub.PendingType is not null &&
            UserSubscriptionTypes.TypeMap.TryGetValue(sub.PendingType, out var pendingTypeObj))
        {
            var user = await db.Users.FindAsync(sub.UserId);

            sub.Type = sub.PendingType;
            sub.PendingType = null;
            sub.LastCharged = DateTime.UtcNow;
            sub.Renewals += 1;
            sub.StripePaymentFailed = false;

            if (user is not null)
                user.SubscriptionType = sub.Type;

            await db.SaveChangesAsync();

            // Deposit VC reward based on the new (post-change) tier
            await DepositVcRewardAsync(sub.UserId, pendingTypeObj.VcReward,
                rewardFingerprint,
                $"Stripe {pendingTypeObj.Name} Monthly Reward - Thank you!",
                ecoService, db, logger);
            return;
        }

        if (!UserSubscriptionTypes.TypeMap.TryGetValue(sub.Type, out var subTypeObj))
            return;

        sub.LastCharged = DateTime.UtcNow;
        sub.Renewals += 1;
        sub.StripePaymentFailed = false;
        await db.SaveChangesAsync();

        // Deposit monthly VC reward
        await DepositVcRewardAsync(sub.UserId, subTypeObj.VcReward,
            rewardFingerprint,
            $"Stripe {subTypeObj.Name} Monthly Reward - Thank you!",
            ecoService, db, logger);
    }

    /// <summary>
    /// Handles customer.subscription.deleted — cancellation or expiry
    /// </summary>
    private static async Task HandleSubscriptionDeletedAsync(Subscription subscription, ValourDb db, ILogger logger)
    {
        var sub = await db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscription.Id);
        if (sub is null)
            return;

        sub.Active = false;

        var user = await db.Users.FindAsync(sub.UserId);
        if (user is not null)
            user.SubscriptionType = null;

        await db.SaveChangesAsync();
        logger.LogInformation("Stripe subscription deletion synced for UserId={UserId}", sub.UserId);
    }

    /// <summary>
    /// Handles invoice.payment_failed — a renewal charge failed
    /// </summary>
    private static async Task HandleInvoicePaymentFailedAsync(Invoice invoice, ValourDb db, ILogger logger)
    {
        var stripeSubscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (string.IsNullOrEmpty(stripeSubscriptionId))
            return;

        var sub = await db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.StripeSubscriptionId == stripeSubscriptionId);
        if (sub is null)
            return;

        sub.StripePaymentFailed = true;
        await db.SaveChangesAsync();

        logger.LogWarning("Stripe payment failed for subscription owner UserId={UserId}", sub.UserId);
    }

    /// <summary>
    /// Handles customer.subscription.updated — syncs external changes (e.g. dashboard cancellation)
    /// </summary>
    private static async Task HandleSubscriptionUpdatedAsync(Subscription subscription, ValourDb db, ILogger logger)
    {
        var sub = await db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.StripeSubscriptionId == subscription.Id);
        if (sub is null)
            return;

        // Sync cancel_at_period_end from Stripe
        sub.Cancelled = subscription.CancelAtPeriodEnd;
        await db.SaveChangesAsync();
        logger.LogInformation("Stripe subscription update synced for UserId={UserId}, CancelAtPeriodEnd={CancelAtPeriodEnd}",
            sub.UserId, subscription.CancelAtPeriodEnd);
    }

    /// <summary>
    /// Deposits a VC reward for a Stripe subscription event
    /// </summary>
    private static async Task DepositVcRewardAsync(
        long userId, int amount, string fingerprint, string description,
        EcoService ecoService, ValourDb db, ILogger logger)
    {
        if (amount <= 0)
            return;

        // Idempotency check
        var alreadyProcessed = await db.Transactions.AnyAsync(x => x.Fingerprint == fingerprint);
        if (alreadyProcessed)
            return;

        var accountTo = await ecoService.GetGlobalAccountAsync(userId);
        if (accountTo is null)
        {
            logger.LogWarning("Stripe reward skipped because user has no global account. UserId={UserId}", userId);
            return;
        }

        var transaction = new Transaction()
        {
            Id = Guid.NewGuid().ToString(),
            PlanetId = ISharedPlanet.ValourCentralId,
            UserFromId = ISharedUser.VictorUserId,
            AccountFromId = 21365328233627648,
            UserToId = userId,
            AccountToId = accountTo.Id,
            TimeStamp = DateTime.UtcNow,
            Description = description,
            Amount = amount,
            Fingerprint = fingerprint,
        };

        var result = await ecoService.ProcessTransactionAsync(transaction);
        if (!result.Success)
        {
            logger.LogWarning("Stripe reward transaction failed for UserId={UserId}. Message={Message}",
                userId, result.Message);
        }
    }

    /// <summary>
    /// Handles checkout.session.completed for one-time VC purchases
    /// </summary>
    public static Task FulfillSessionAsync(Session session, EcoService ecoService, ValourDb db)
    {
        return FulfillSessionAsync(session, ecoService, db, null);
    }

    public static async Task FulfillSessionAsync(
        Session session,
        EcoService ecoService,
        ValourDb db,
        ILogger logger)
    {
        var metadata = session.Metadata;
        if (metadata is null ||
            !metadata.TryGetValue("userId", out var userIdStr) ||
            !metadata.TryGetValue("creditAmount", out var creditAmountStr))
        {
            logger?.LogWarning("Stripe one-time checkout fulfillment skipped due to missing metadata.");
            return;
        }

        if (!long.TryParse(userIdStr, out var userId) ||
            !int.TryParse(creditAmountStr, out var creditAmount))
        {
            logger?.LogWarning("Stripe one-time checkout fulfillment skipped due to invalid metadata values.");
            return;
        }

        if (string.IsNullOrEmpty(session.Id))
        {
            logger?.LogWarning("Stripe one-time checkout fulfillment skipped due to missing session ID.");
            return;
        }

        var checkoutFingerprint = BuildOpaqueStripeFingerprint("stripe_checkout", session.Id);

        // Idempotency: check if already processed using deterministic internal fingerprint
        var alreadyProcessed = await db.Transactions.AnyAsync(x => x.Fingerprint == checkoutFingerprint);
        if (alreadyProcessed)
            return;

        var accountTo = await ecoService.GetGlobalAccountAsync(userId);
        if (accountTo is null)
        {
            logger?.LogWarning("Stripe one-time checkout fulfillment skipped because user has no global account. UserId={UserId}",
                userId);
            return;
        }

        var transaction = new Transaction()
        {
            Id = Guid.NewGuid().ToString(),
            PlanetId = ISharedPlanet.ValourCentralId,
            UserFromId = ISharedUser.VictorUserId,
            AccountFromId = 21365328233627648,
            UserToId = userId,
            AccountToId = accountTo.Id,
            TimeStamp = DateTime.UtcNow,
            Description = "Online Purchase - Thank you!",
            Amount = creditAmount,
            Fingerprint = checkoutFingerprint,
        };

        var result = await ecoService.ProcessTransactionAsync(transaction);
        if (!result.Success)
        {
            logger?.LogWarning("Stripe one-time checkout fulfillment transaction failed for UserId={UserId}. Message={Message}",
                userId, result.Message);
        }
    }

    private static string BuildOpaqueStripeFingerprint(string prefix, string stripeIdentifier)
    {
        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "stripe" : prefix.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(stripeIdentifier))
            return $"{safePrefix}:missing";

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(stripeIdentifier));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return $"{safePrefix}:{hash[..24]}";
    }
}
