using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Services;

public class SubscriptionService
{
    private readonly ValourDB _db;
    private readonly ILogger<SubscriptionService> _logger;
    
    public SubscriptionService(ValourDB db, ILogger<SubscriptionService> logger)
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
        var currentSub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.Active && x.UserId == userId);
        
        // get subscription type
        var subTypeObj = UserSubscriptionTypes.TypeMap[subType];

        if (currentSub is null)
        {
            return subTypeObj.Price;
        }
        else
        {
            return Math.Max(subTypeObj.Price - UserSubscriptionTypes.TypeMap[currentSub.Type].Price, 0);
        }
    }

    /// <summary>
    /// Starts a subscription with the given user and subscription type
    /// </summary>
    public async Task<TaskResult> StartSubscriptionAsync(long userId, string subType)
    {
        // get user
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
        {
            // skip if user is null (should not happen)
            return new TaskResult(false, "User not found");
        }

        var currentSub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(x => x.Active && x.UserId == userId);

        if (currentSub is not null && currentSub.Type == subType && !currentSub.Cancelled)
        {
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
        
        // get subscription type
        var subTypeObj = UserSubscriptionTypes.TypeMap[subType];
        
        // account for upgrading from a current tier
        var cost = subTypeObj.Price;

        if (currentSub is not null)
        {
            // Make cost the difference
            cost -= UserSubscriptionTypes.TypeMap[currentSub.Type].Price;
        }

        // No refunds
        if (cost < 0)
            cost = 0;
        
        // check if user has enough balance for subscription
        if (userAccount.BalanceValue < subTypeObj.Price)
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
            if (currentSub.Type == subType)
            {
                currentSub.Cancelled = false;
                createSub = false;
            }
            // need entirely new sub for new type
            else
            {
                currentSub.Active = false;
            }
        }
        
        // remove balance from user
        userAccount.BalanceValue -= cost;

        user.SubscriptionType = subType;
        
        // create subscription
        
        Valour.Database.UserSubscription newSub = null;

        if (createSub)
        {
            newSub = new()
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Type = subType,
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

        // get all active subscriptions that are due
        var dueSubs = await _db.UserSubscriptions.Where(
                x => x.Active // must be active
                     && (x.LastCharged.Month != now.Month // must be new month
                         && (now.Day >= // current date needs to be the same or after
                             (x.LastCharged.Day > 29
                                 ? 29 // If the last charge was on the 30th or 31st, charge on the 29th.
                                      // Why? Because some months don't have 30 or 31 days.
                                 : x.LastCharged.Day)))) // must be the same day of month or after
            .ToListAsync();
        
        // now we have all the subscriptions that are due
        // we need to charge them or cancel them
        foreach (var sub in dueSubs)
        {
            var subType = UserSubscriptionTypes.TypeMap[sub.Type];
            
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
                // check if user has enough balance for subscription
                // also disable if marked as cancelled
                if (userAccount is null || userAccount.BalanceValue < subType.Price || sub.Cancelled)
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
}