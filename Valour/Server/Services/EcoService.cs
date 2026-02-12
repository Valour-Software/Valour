using System.Collections.Concurrent;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Services;

/// <summary>
/// The EcoService handles economic transactions and trading for platform-wide
/// and planet economies
/// </summary>
public class EcoService
{
    private readonly ValourDb _db;
    private readonly ILogger<EcoService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly NotificationService _notificationService;

    /// <summary>
    /// Cache for currency definitions
    /// </summary>
    private readonly ConcurrentDictionary<long, Currency> _currencyCache = new();

    public EcoService(
        ValourDb db, 
        ILogger<EcoService> logger, 
        CoreHubService coreHub, 
        NodeLifecycleService nodeLifecycleService, 
        NotificationService notificationService)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
        _nodeLifecycleService = nodeLifecycleService;
        _notificationService = notificationService;
    }

    ////////////////
    // Currencies //
    ////////////////

    public async ValueTask<Currency> GetPlanetCurrencyAsync(long planetId) => 
        (await _db.Currencies.FirstOrDefaultAsync(x => x.PlanetId == planetId)).ToModel();

    /// <summary>
    /// Returns the currency with the given id
    /// </summary>
    public async ValueTask<Currency> GetCurrencyAsync(long id) {

        _currencyCache.TryGetValue(id, out var currency);

        if (currency is null)
        {
           currency = (await _db.Currencies.FindAsync(id)).ToModel();

            if (currency is not null)
                _currencyCache.TryAdd(id, currency);
        }

        return currency;
    }

    /// <summary>
    /// Creates the given currency
    /// </summary>
    public async Task<TaskResult<Currency>> CreateCurrencyAsync(Currency newCurrency)
    {
        if (newCurrency is null)
            return new TaskResult<Currency>(false, "Given value is null");

        var exists = await _db.Currencies.AnyAsync(x => x.PlanetId == newCurrency.PlanetId);

        if (exists)
            return new TaskResult<Currency>(false, "Planet already has a currency");

        var planetExists = await _db.Planets.AnyAsync(x => x.Id == newCurrency.PlanetId);
        if (!planetExists)
            return new TaskResult<Currency>(false, "Planet does not exist");

        var validation = ValidateCurrency(newCurrency);
        if (!validation.Success)
            return new TaskResult<Currency>(false, validation.Message);

        newCurrency.Id = IdManager.Generate();
        newCurrency.Issued = 0; // Issuance is managed via other means
        var dbCurrency = newCurrency.ToDatabase();

        var tran = await _db.Database.BeginTransactionAsync();
        
        try
        {
            await _db.Currencies.AddAsync(dbCurrency);
            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError("Error adding currency: {Message}", e.Message);
            return new TaskResult<Currency>(false, "Error adding currency");
        }

        _coreHub.NotifyCurrencyChange(newCurrency);

        return new TaskResult<Currency>(true, "Currency added successfully", newCurrency);
    }

    /// <summary>
    /// Updates the given currency
    /// </summary>
    public async Task<TaskResult<Currency>> UpdateCurrencyAsync(Currency updated)
    {
        var validation = ValidateCurrency(updated);
        if (!validation.Success)
            return new TaskResult<Currency>(false, validation.Message);

        var old = await _db.Currencies.FindAsync(updated.Id);
        if (old is null)
            return new TaskResult<Currency>(false, "Currency not found");

        if (updated.PlanetId != old.PlanetId)
            return new TaskResult<Currency>(false, "Planet Id cannot be changed");

        if (updated.DecimalPlaces < old.DecimalPlaces)
            return new TaskResult<Currency>(false, "You cannot remove decimals");

        if (updated.Issued != old.Issued)
            return new TaskResult<Currency>(false, "You cannot directly edit the issued amount");

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.Entry(old).CurrentValues.SetValues(updated);
            _db.Currencies.Update(old);
            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, "Error updating currency: {Message}", e.Message);
            return new TaskResult<Currency>(false, "Error updating currency");
        }

        _currencyCache[updated.Id] = updated;

        _coreHub.NotifyCurrencyChange(updated);

        return new TaskResult<Currency>(true, "Currency updated successfully", updated);
    }

    /// <summary>
    /// Validates the state of a currency
    /// </summary>
    public static TaskResult ValidateCurrency(Currency currency)
    {
        if (currency is null)
            return TaskResult.FromFailure("Given value is null");

        if (string.IsNullOrWhiteSpace(currency.Name))
            return TaskResult.FromFailure("Currency must have a name");

        if (currency.Name.Length > 20)
            return TaskResult.FromFailure("Max name length is 20 characters");

        if (string.IsNullOrWhiteSpace(currency.PluralName))
            return TaskResult.FromFailure("Currency must have a plural name");

        if (currency.PluralName.Length > 20)
            return TaskResult.FromFailure("Max name plural length is 20 characters");

        if (string.IsNullOrWhiteSpace(currency.ShortCode))
            return TaskResult.FromFailure("Currency must have a shortcode");

        if (currency.ShortCode.Length > 5)
            return TaskResult.FromFailure("Max shortcode length is 5 characters");

        if (string.IsNullOrWhiteSpace(currency.Symbol))
            return TaskResult.FromFailure("Currency must have a symbol");

        if (currency.Symbol.Length > 5)
            return TaskResult.FromFailure("Max symbol length is 5 characters");

        if (currency.DecimalPlaces > 8)
            return TaskResult.FromFailure("Currency can have max 8 decimals");

        if (currency.DecimalPlaces < 0)
            return TaskResult.FromFailure("Negative decimals are not allowed");

        return TaskResult.SuccessResult;
    }

    //////////////
    // Accounts //
    //////////////

    /// <summary>
    /// Returns the account with the given id
    /// </summary>
    public async ValueTask<EcoAccount> GetAccountAsync(long id) =>
        (await _db.EcoAccounts.FindAsync(id)).ToModel();

    /// <summary>
    /// Returns the user account with the given user and planet ids
    /// </summary>
    public async Task<EcoAccount> GetUserAccountAsync(long userId, long planetId) =>
        (await _db.EcoAccounts.FirstOrDefaultAsync(x => x.UserId == userId && x.PlanetId == planetId && x.AccountType == AccountType.User)).ToModel();

    /// <summary>
    /// Returns the shared accounts for the given planet id
    /// </summary>
    public async Task<QueryResponse<EcoAccount>> GetPlanetSharedAccountsAsync(long planetId, int skip = 0,
        int take = 50)
    {
        var baseQuery = _db.EcoAccounts
            .AsNoTracking()
            .Where(x => x.AccountType == AccountType.Shared && x.PlanetId == planetId)
            .OrderByDescending(x => x.BalanceValue);

        var total = await baseQuery.CountAsync();
        
        var items = await baseQuery
            .Skip(skip)
            .Take(take)
            .Select(x => x.ToModel())
            .ToListAsync();

        return new QueryResponse<EcoAccount>()
        {
            TotalCount = total,
            Items = items
        };
    }

    /// <summary>
    /// Returns the user accounts for the given planet id
    /// </summary>
    public async Task<QueryResponse<EcoAccount>> GetPlanetUserAccountsAsync(long planetId, int skip = 0, int take = 50)
    {
        var baseQuery = _db.EcoAccounts
            .AsNoTracking()
            .Where(x => x.AccountType == AccountType.User && x.PlanetId == planetId)
            .OrderByDescending(x => x.BalanceValue);
            
        var total = await baseQuery.CountAsync();
        
        var items = await baseQuery
            .Skip(skip)
            .Take(take)
            .Select(x => x.ToModel())
            .ToListAsync();

        return new QueryResponse<EcoAccount>()
        {
            TotalCount = total,
            Items = items
        };
    }

    /// <summary>
    /// Returns the user accounts for the given planet id
    /// </summary>
    public async ValueTask<QueryResponse<EcoAccountPlanetMember>> GetPlanetUserAccountMembersAsync(long planetId,
        int skip = 0, int take = 50)
    {
        var baseQuery = 
            _db.EcoAccounts
                .AsNoTracking()
                .Include(x => x.PlanetMember)
                    .ThenInclude(x => x.User)
                .Where(x => x.AccountType == AccountType.User && x.PlanetId == planetId &&
                                                         x.PlanetMemberId != null)
                .Where(x => !x.PlanetMember.IsDeleted)
                .OrderByDescending(x => x.BalanceValue)
                    .ThenByDescending(x => x.Id);
        
        var total = await baseQuery.CountAsync();
        
        var items = await baseQuery
            .Skip(skip)
            .Take(take)
            .Select(x => new EcoAccountPlanetMember()
            {
                Account = x.ToModel(),
                Member = x.PlanetMember.ToModel()
            })
            .ToListAsync();

        return new QueryResponse<EcoAccountPlanetMember>()
        {
            TotalCount = total,
            Items = items
        };
    }

    /// <summary>
    /// Returns the planet accounts the given user can send to
    /// </summary>
    public async ValueTask<List<EcoAccountSearchResult>> GetPlanetAccountsCanSendAsync(long planetId, long accountId,
        string filter = "")
    {
        filter = filter?.ToLower() ?? "";

        IQueryable<Valour.Database.Economy.EcoAccount> query = _db.EcoAccounts
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId && x.Id != accountId)
            .Include(x => x.User);
        
        if (!string.IsNullOrEmpty(filter))
            query = query.Where(x => x.Name.ToLower().Contains(filter) || 
                               (x.AccountType == AccountType.User && x.User.Name.ToLower().Contains(filter)));
        
        return await query.Take(50).Select(x => new EcoAccountSearchResult()
        {
            Account = x.ToModel(),
            Name = x.AccountType == AccountType.User ? x.User.Name : x.Name
        }).ToListAsync();
    }

    /// <summary>
    /// Returns all accounts associated with a user id
    /// </summary>
    public async ValueTask<List<EcoAccount>> GetAccountsAsync(long userId) =>
        await _db.EcoAccounts.Where(x => x.UserId == userId).Select(x => x.ToModel()).ToListAsync();
    
    /// <summary>
    /// Returns the global account associated with a user id
    /// </summary>
    public async ValueTask<EcoAccount> GetGlobalAccountAsync(long userId) =>
        (await _db.EcoAccounts.FirstOrDefaultAsync(x => x.UserId == userId && x.CurrencyId == ISharedCurrency.ValourCreditsId && x.AccountType == AccountType.User)).ToModel();
    
    
    public async ValueTask<TaskResult<EcoAccount>> CreateEcoAccountAsync(EcoAccount account)
    {
        if (account is null)
            return new TaskResult<EcoAccount>(false, "Account is null");

        var planet = await _db.Planets.FindAsync(account.PlanetId);
        if (planet is null)
            return new TaskResult<EcoAccount>(false, "Planet not found");

        var member =
            await _db.PlanetMembers.FirstOrDefaultAsync(x =>
                x.UserId == account.UserId && x.PlanetId == account.PlanetId);
        
        if (member is null)
            return new TaskResult<EcoAccount>(false, "User is not a member of the planet");

        account.PlanetMemberId = member.Id;
        
        if (string.IsNullOrWhiteSpace(account.Name))
        {
            account.Name = account.Id.ToString();
        }
        
        if (account.Name.Length > 20)
            return new TaskResult<EcoAccount>(false, "Max account name length is 20");
        
        if (account.BalanceValue > 0)
            return new TaskResult<EcoAccount>(false, "Initial balance must be zero");

        if (account.AccountType == AccountType.User)
        {
            if (await _db.EcoAccounts.AnyAsync(x => x.UserId == account.UserId && x.PlanetId == account.PlanetId && x.AccountType == AccountType.User))
                return new TaskResult<EcoAccount>(false, "User already has an account on this planet");
        }

        if (!await _db.Currencies.AnyAsync(x => x.PlanetId == account.PlanetId && x.Id == account.CurrencyId))
            return new TaskResult<EcoAccount>(false, "Currency is invalid for given planet");

        account.Id = IdManager.Generate();
        
        await using var tran = await _db.Database.BeginTransactionAsync();
        
        try
        {
            await _db.EcoAccounts.AddAsync(account.ToDatabase());
            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, "Error creating account: {Message}", e.Message);
            return new TaskResult<EcoAccount>(false, "Error creating account");
        }
        
        return new TaskResult<EcoAccount>(true, "Account created successfully", account);
    }

    public async Task<TaskResult<EcoAccount>> UpdateEcoAccountAsync(EcoAccount account)
    {
        var old = await _db.EcoAccounts.FindAsync(account.Id);
        if (old is null)
            return new TaskResult<EcoAccount>(false, "Account not found");
        
        // Literally the only thing you can change is the name so we're just going to copy that across
        // rather than validate 50 things for no reason. If you're trying to change something else and
        // there's no error this is why.
        
        if (account.Name.Length > 20)
            return new TaskResult<EcoAccount>(false, "Max account name length is 20");
        
        old.Name = account.Name;

        await _db.SaveChangesAsync();
        
        return new TaskResult<EcoAccount>(true, "Account updated successfully", old.ToModel());
    }

    public async Task<TaskResult> DeleteEcoAccountAsync(long accountId)
    {
        var account = await _db.EcoAccounts.FindAsync(accountId);
        
        if (account is null)
            return new TaskResult(false, "Account not found");
        
        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            _db.EcoAccounts.Remove(account);
            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, "Error deleting account: {Message}", e.Message);
            return new TaskResult(false, "Error deleting account");
        }

        return new TaskResult(true, "Account deleted successfully");
    }

    //////////////////
    // Transactions //
    //////////////////

    /// <summary>
    /// Returns the transaction with the given id
    /// </summary>
    public async ValueTask<Transaction> GetTransactionAsync(string id) =>
        (await _db.Transactions.FindAsync(id)).ToModel();
    
    
    /// <summary>
    /// Receipts do not change so we cache them aggressively
    /// </summary>
    private static ConcurrentDictionary<string, EcoReceipt> _receiptCache = new();

    public async ValueTask<EcoReceipt> GetReceiptAsync(string transactionId)
    {
        if (!_receiptCache.TryGetValue(transactionId, out var cached))
        {

            var transaction = await _db.Transactions
                .AsNoTracking()
                .Include(x => x.UserFrom)
                .Include(x => x.UserTo)
                .Include(x => x.AccountFrom)
                .Include(x => x.AccountTo)
                .FirstOrDefaultAsync(x => x.Id == transactionId);

            if (transaction is null)
                return null;

            var currency = await GetCurrencyAsync(transaction.AccountFrom.CurrencyId);

            var receipt = new EcoReceipt()
            {
                UserFromId = transaction.UserFromId,
                UserToId = transaction.UserToId,
                Currency = currency,
                Amount = transaction.Amount,
                TimeStamp = transaction.TimeStamp,
                AccountFromId = transaction.AccountFromId,
                AccountToId = transaction.AccountToId,
                TransactionId = transactionId,
                AccountFromName = transaction.AccountFrom.Name,
                AccountToName = transaction.AccountTo.Name,
            };

            if (transaction.AccountFrom.AccountType == AccountType.User)
            {
                receipt.AccountFromName = transaction.UserFrom.Name;
            }

            if (transaction.AccountTo.AccountType == AccountType.User)
            {
                receipt.AccountToName = transaction.UserTo.Name;
            }
            
            _receiptCache[transactionId] = receipt;
            return receipt;
        }
        else
        {
            return cached;
        }
    }

    /// <summary>
    /// Returns the last (count) transactions for the given account id
    /// </summary>
    public async Task<List<Transaction>> GetLastTransactionsAsync(long accountId, int count = 50)
    {
        // Get transactions in either direction ordered by time
        return await _db.Transactions
            .AsNoTracking()
            .Where(x => x.AccountFromId == accountId || x.AccountToId == accountId)
            .OrderByDescending(x => x.TimeStamp)
            .Select(x => x.ToModel())
            .ToListAsync();
    }

    public async ValueTask<TaskResult<Transaction>> CreateTransactionAsync(Transaction transaction, bool issuing = false)
    {
        if (transaction is null)
            return new TaskResult<Transaction>(false, "Null transaction");
        
        if (!issuing && transaction.AccountFromId == transaction.AccountToId)
            return new TaskResult<Transaction>(false, "Cannot send to self");
        
        // Set id before processing
        transaction.Id = Guid.NewGuid().ToString();
        transaction.TimeStamp = DateTime.UtcNow;

        // Post transaction to queue
        var result = await ProcessTransactionAsync(transaction, issuing);
        if (!result.Success)
            return new TaskResult<Transaction>(false, result.Message);
        
        return new TaskResult<Transaction>(true, result.Message, transaction);
    }
    
    public async Task<TaskResult> ProcessTransactionAsync(Transaction transaction, bool issuing = false)
    {
        // Fun case for those who wish to break the system
        // Throwbacks to SV1
        if (transaction.Amount <= 0)
            return new TaskResult(false, "Amount must be positive");

        // Global Valour Credits transaction
        var isGlobal = (transaction.PlanetId == ISharedPlanet.ValourCentralId);

        if (!issuing && (transaction.AccountFromId == transaction.AccountToId))
            return new TaskResult(false, "Cannot send to self");
        
        var fromAcc = await _db.EcoAccounts.FindAsync(transaction.AccountFromId);
        if (fromAcc is null)
            return new TaskResult(false, "Could not find sending account");
        
        var toAcc = await _db.EcoAccounts.FindAsync(transaction.AccountToId);
        if (toAcc is null)
            return new TaskResult(false, "Could not find receiving account");

        if (fromAcc.CurrencyId != toAcc.CurrencyId)
            return new TaskResult(false, "Currency mismatch");

        // Get currency from sending account. Both should be the same anyways.
        var currency = await GetCurrencyAsync(fromAcc.CurrencyId);

        // Get amount rounded to decimals allowed by currency
        transaction.Amount = Math.Round(transaction.Amount, currency.DecimalPlaces);
        
        // Make sure sender isn't too broke
        // First check is for issuing, second is for normal transactions
        if (!issuing && transaction.Amount > fromAcc.BalanceValue)
            return new TaskResult(false, "Sender cannot afford this transaction");

        await using var trans = await _db.Database.BeginTransactionAsync();

        if (string.IsNullOrWhiteSpace(transaction.Id))
        {
            transaction.Id = Guid.NewGuid().ToString();
        }

        // Do not remove funds from sender if issuing
        if (!issuing)
        {
            fromAcc.BalanceValue -= transaction.Amount;
        }

        toAcc.BalanceValue += transaction.Amount;

        try
        {
            // Build transaction for database
            var dbTrans = transaction.ToDatabase();
            _db.Transactions.Add(dbTrans);

            if (issuing)
            {
                currency.Issued += (long)transaction.Amount;
                var dbCurrency = await _db.Currencies.FindAsync(currency.Id);
                if (dbCurrency is null)
                    return new TaskResult(false, "Could not find currency");
                
                dbCurrency.Issued += (long) transaction.Amount;
            }

            await _db.SaveChangesAsync();
            await trans.CommitAsync();
        }
        catch (DbUpdateConcurrencyException e)
        {
            await trans.RollbackAsync();
            return new TaskResult(false, "Another transaction modified your account before processing finished. Please try again.");
        }
        catch(Exception e)
        {
            await trans.RollbackAsync();
            return new TaskResult(false, "Error: " + e.Message);
        }

        if (isGlobal)
        {
            await _coreHub.RelayTransaction(transaction, _nodeLifecycleService);
        }
        else
        {
            _coreHub.NotifyPlanetTransactionProcessed(transaction);
        }
        
        var userFrom = await _db.Users.FindAsync(transaction.UserFromId);

        // Send notification
        await _notificationService.SendUserNotification(transaction.UserToId, new Notification()
        {
            UserId = transaction.UserToId,
            Title = $"{userFrom.Name} sent you {currency.Format(transaction.Amount)}!",
            PlanetId = transaction.PlanetId,
            SourceId = transaction.UserFromId,
            Source = NotificationSource.TransactionReceived,
            Body = transaction.Description,
            ImageUrl = userFrom.GetAvatarUrl(AvatarFormat.Webp128),
            ClickUrl = $"/receipt/{transaction.Id}"
        });

        return new TaskResult(true, "api/eco/transactions/" + transaction.Id);
    }
}
