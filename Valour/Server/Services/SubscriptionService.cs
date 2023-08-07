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

    /// <summary>
    /// Starts a subscription with the given user and subscription type
    /// </summary>
    public async Task<TaskResult> StartSubscription(long userId, string subType)
    {
        // get user
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
        {
            // skip if user is null (should not happen)
            return new TaskResult(false, "User not found");
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
        
        // check if user has enough balance for subscription
        if (userAccount.BalanceValue < subTypeObj.Price)
        {
            return new TaskResult(false, "Insufficient funds.");
        }
        
        await using var transaction = await _db.Database.BeginTransactionAsync();
        
        // remove balance from user
        userAccount.BalanceValue -= subTypeObj.Price;
        
        // create subscription
        Valour.Database.UserSubscription newSub = new()
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Type = subType,
            Active = true,
            LastCharged = DateTime.UtcNow,
            Renewals = 0
        };
        
        // add to database
        try
        {
            await _db.UserSubscriptions.AddAsync(newSub);
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
}