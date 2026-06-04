using Valour.Server.Api.Dynamic;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Services;

public class SubscriptionService
{
    private readonly ValourDb _db;
    private readonly ILogger<SubscriptionService> _logger;
    
    public SubscriptionService(ValourDb db, ILogger<SubscriptionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UserSubscription> GetActiveSubscriptionAsync(long userId)
    {
        return (await _db.UserSubscriptions.FirstOrDefaultAsync(x => x.Active && x.UserId == userId)).ToModel();
    }

    public async Task<decimal> GetSubscriptionPriceAsync(long userId, string subType)
    {
        if (!UserSubscriptionTypes.TypeMap.TryGetValue(subType, out var subTypeObj))
            return 0;

        var currentSub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.Active && x.UserId == userId);

        if (currentSub is null)
        {
            return subTypeObj.Price;
        }
        else
        {
            if (!UserSubscriptionTypes.TypeMap.TryGetValue(currentSub.Type, out var currentTypeObj))
                return subTypeObj.Price;

            return Math.Max(subTypeObj.Price - currentTypeObj.Price, 0);
        }
    }

    /// <summary>
    /// Starts a subscription with the given user and subscription type
    /// </summary>
    public async Task<TaskResult> StartSubscriptionAsync(long userId, string subType)
    {
        if (!UserSubscriptionTypes.TypeMap.TryGetValue(subType, out var subTypeObj))
            return new TaskResult(false, "Unknown subscription type.");

        var normalizedSubType = subTypeObj.Name;

        // get user
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
        {
            // skip if user is null (should not happen)
            return new TaskResult(false, "User not found");
        }

        var currentSub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.Active && x.UserId == userId);

        UserSubscriptionType currentTypeObj = null;
        var currentNormalizedType = currentSub?.Type;
        if (currentSub is not null)
        {
            if (!UserSubscriptionTypes.TypeMap.TryGetValue(currentSub.Type, out currentTypeObj))
                return new TaskResult(false, "Current subscription type is invalid.");

            currentNormalizedType = currentTypeObj.Name;
        }

        if (currentSub is not null && currentSub.StripeSubscriptionId != null)
        {
            return new TaskResult(false, "You have an active Stripe subscription. Please cancel it first.");
        }

        // If user selects their current tier while a pending change exists, cancel the pending change
        if (currentSub is not null && currentNormalizedType == normalizedSubType && !currentSub.Cancelled)
        {
            if (currentSub.PendingType is not null)
            {
                currentSub.PendingType = null;
                if (currentSub.Type != normalizedSubType)
                    currentSub.Type = normalizedSubType;
                await _db.SaveChangesAsync();
                return new TaskResult(true, "Pending tier change cancelled.");
            }
            return new TaskResult(false, "You already have this subscription.");
        }

        // check VC balance of user
        var userAccount = await _db.EcoAccounts
            .FirstOrDefaultAsync(x =>
                x.UserId == userId &&
                x.CurrencyId == ISharedCurrency.ValourCreditsId && // Valour Credits
                x.AccountType == AccountType.User);

        if (userAccount is null)
        {
            return new TaskResult(false, "Valour Credits account not found.");
        }

        // account for upgrading from a current tier
        var cost = subTypeObj.Price;

        if (currentSub is not null)
        {
            // Make cost the difference
            cost -= currentTypeObj.Price;
        }

        // No refunds
        if (cost < 0)
            cost = 0;

        // Downgrade path: cost is 0 but tiers differ → schedule for next billing cycle
        if (cost == 0 && currentSub is not null && currentNormalizedType != normalizedSubType)
        {
            currentSub.PendingType = normalizedSubType;
            await _db.SaveChangesAsync();
            return new TaskResult(true, $"Your plan will change to {normalizedSubType} at your next billing cycle.");
        }

        // check if user has enough balance for subscription (use prorated cost, not full price)
        if (userAccount.BalanceValue < cost)
        {
            return new TaskResult(false, "Insufficient funds.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        // disable active subscription
        // why don't we just change the type?
        // because we want to keep track of the history of subscriptions
        // and we wouldn't know the total sub value if the type is just changed

        bool createSub = true;

        if (currentSub is not null)
        {
            // Un-cancel
            if (currentNormalizedType == normalizedSubType)
            {
                currentSub.Type = normalizedSubType;
                currentSub.Cancelled = false;
                createSub = false;
            }
            // need entirely new sub for new type
            else
            {
                currentSub.Active = false;
                currentSub.PendingType = null; // clear any pending change on the old sub
            }
        }

        // remove balance from user
        userAccount.BalanceValue -= cost;

        user.SubscriptionType = normalizedSubType;

        // create subscription

        Valour.Database.UserSubscription newSub = null;

        if (createSub)
        {
            newSub = new()
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Type = normalizedSubType,
                Active = true,
                LastCharged = DateTime.UtcNow,
                Renewals = 0,
                Created = DateTime.UtcNow,
            };
        }

        // add to database
        try
        {
            if (createSub)
            {
                await _db.UserSubscriptions.AddAsync(newSub);
            }

            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();

            _logger.LogError(e, "Failed to add subscription to database");
            return new TaskResult(false, "Failed to add subscription to database");
        }

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> EndSubscriptionAsync(long userId)
    {
        var currentSub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.Active && x.UserId == userId);

        if (currentSub is null)
            return new TaskResult(false, "Could not find any active subscriptions.");

        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return new TaskResult(false, "Could not find user.");
        
        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            currentSub.Cancelled = true;
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (System.Exception e)
        {
            await transaction.RollbackAsync();
            
            _logger.LogError(e, "Failed to remove subscription from database");
            return new TaskResult(false, "Failed to remove subscription from database");
        }

        return new TaskResult(true, "Successfully removed subscription");
    }

    /// <summary>
    /// Processes all active subscriptions that are due
    /// </summary>
    public async Task ProcessActiveDue()
    {
        // current time
        var now = DateTime.UtcNow;

        var activeVcSubs = await _db.UserSubscriptions
            .Where(x => x.Active && x.StripeSubscriptionId == null)
            .ToListAsync();

        var dueSubs = activeVcSubs
            .Where(x => IsSubscriptionDue(x.LastCharged, now))
            .ToList();

        // now we have all the subscriptions that are due
        // we need to charge them or cancel them
        foreach (var sub in dueSubs)
        {
            // get user
            var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == sub.UserId);
            if (user is null)
            {
                // skip if user is null (should not happen)
                continue;
            }

            // check VC balance of user
            var userAccount = await _db.EcoAccounts
                .FirstOrDefaultAsync(x =>
                    x.UserId == sub.UserId &&
                    x.CurrencyId == ISharedCurrency.ValourCreditsId && // Valour Credits
                    x.AccountType == AccountType.User);

            try
            {
                // If cancelled, deactivate regardless of pending (ignore pending on cancel)
                if (sub.Cancelled)
                {
                    sub.Active = false;
                    sub.PendingType = null;
                    user.SubscriptionType = null;
                    await _db.SaveChangesAsync();

                    _logger.LogInformation(
                        "Subscription {SubId} for user {UserId} of type {SubType} was deactivated (cancelled)",
                        sub.Id, sub.UserId, sub.Type);
                    continue;
                }

                // Handle pending tier change at renewal
                if (sub.PendingType is not null)
                {
                    if (!UserSubscriptionTypes.TypeMap.TryGetValue(sub.PendingType, out var pendingTypeObj))
                    {
                        // Invalid pending type, clear it and continue with normal renewal
                        sub.PendingType = null;
                    }
                    else
                    {
                        // Deactivate current sub and create new one with the pending type
                        if (userAccount is null || userAccount.BalanceValue < pendingTypeObj.Price)
                        {
                            sub.Active = false;
                            sub.PendingType = null;
                            user.SubscriptionType = null;
                            await _db.SaveChangesAsync();

                            _logger.LogInformation(
                                "Subscription {SubId} for user {UserId} pending change to {PendingType} failed - insufficient funds",
                                sub.Id, sub.UserId, sub.PendingType);
                            continue;
                        }

                        // Charge at new tier price
                        userAccount.BalanceValue -= pendingTypeObj.Price;

                        // Deactivate old sub
                        sub.Active = false;
                        sub.PendingType = null;

                        // Create new sub with the pending type
                        var newSub = new Valour.Database.UserSubscription
                        {
                            Id = Guid.NewGuid().ToString(),
                            UserId = sub.UserId,
                            Type = pendingTypeObj.Name,
                            Active = true,
                            LastCharged = now,
                            Renewals = 0,
                            Created = now,
                        };

                        await _db.UserSubscriptions.AddAsync(newSub);
                        user.SubscriptionType = pendingTypeObj.Name;
                        await _db.SaveChangesAsync();

                        _logger.LogInformation(
                            "Subscription {SubId} for user {UserId} changed from {OldType} to {NewType}",
                            sub.Id, sub.UserId, sub.Type, pendingTypeObj.Name);
                        continue;
                    }
                }

                if (!UserSubscriptionTypes.TypeMap.TryGetValue(sub.Type, out var subType))
                {
                    sub.Active = false;
                    user.SubscriptionType = null;
                    await _db.SaveChangesAsync();
                    _logger.LogWarning(
                        "Subscription {SubId} for user {UserId} has invalid type {SubType} and was deactivated",
                        sub.Id, sub.UserId, sub.Type);
                    continue;
                }

                // check if user has enough balance for subscription
                if (userAccount is null || userAccount.BalanceValue < subType.Price)
                {
                    // cancel subscription
                    sub.Active = false;
                    user.SubscriptionType = null;
                    await _db.SaveChangesAsync();

                    // log
                    _logger.LogInformation(
                        "Subscription {SubId} for user {UserId} of type {SubType} was cancelled due to insufficient funds",
                        sub.Id, sub.UserId, sub.Type);

                    continue;
                }
                else
                {
                    // remove balance from user
                    userAccount.BalanceValue -= subType.Price;
                    sub.LastCharged = now;
                    sub.Renewals += 1;

                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error processing subscription {SubId} for user {UserId} of type {SubType}",
                    sub.Id, sub.UserId, sub.Type);
                continue;
            }
        }
    }

    private static bool IsSubscriptionDue(DateTime lastCharged, DateTime now)
    {
        var nextMonth = new DateTime(lastCharged.Year, lastCharged.Month, 1).AddMonths(1);
        var renewalDay = lastCharged.Day > 29 ? 29 : lastCharged.Day;
        renewalDay = Math.Min(renewalDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
        var renewalDate = new DateTime(nextMonth.Year, nextMonth.Month, renewalDay);

        return now.Date >= renewalDate;
    }

    /// <summary>
    /// Cancels a pending tier change for the user's active subscription.
    /// For Stripe subs, also reverts the Stripe subscription price.
    /// </summary>
    public async Task<TaskResult> CancelPendingChangeAsync(long userId)
    {
        var currentSub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.Active && x.UserId == userId);

        if (currentSub is null)
            return new TaskResult(false, "No active subscription found.");

        if (currentSub.PendingType is null)
            return new TaskResult(false, "No pending tier change to cancel.");

        // If Stripe-managed, revert the price on Stripe back to the current tier
        if (!string.IsNullOrEmpty(currentSub.StripeSubscriptionId))
        {
            if (!UserSubscriptionTypes.TypeMap.TryGetValue(currentSub.Type, out var currentTypeObj))
                return new TaskResult(false, "Current subscription type is invalid.");

            try
            {
                var currentPriceId = await StripeApi.GetOrCreateStripePriceIdForTierAsync(currentTypeObj);

                var stripeService = new Stripe.SubscriptionService();
                var stripeSub = await stripeService.GetAsync(currentSub.StripeSubscriptionId);

                if (stripeSub.Items?.Data != null && stripeSub.Items.Data.Count > 0)
                {
                    var itemId = stripeSub.Items.Data[0].Id;
                    await stripeService.UpdateAsync(currentSub.StripeSubscriptionId, new Stripe.SubscriptionUpdateOptions
                    {
                        Items = new List<Stripe.SubscriptionItemOptions>
                        {
                            new() { Id = itemId, Price = currentPriceId }
                        },
                        ProrationBehavior = "none",
                    });
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to revert Stripe price for subscription {SubId}", currentSub.Id);
                return new TaskResult(false, "Failed to revert Stripe subscription price.");
            }
        }

        currentSub.PendingType = null;
        await _db.SaveChangesAsync();

        return new TaskResult(true, "Pending tier change cancelled.");
    }
}
