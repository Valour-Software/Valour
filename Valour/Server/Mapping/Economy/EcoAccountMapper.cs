namespace Valour.Server.Mapping.Economy;

public static class EcoAccountMapper
{  
    public static EcoAccount ToModel(this Valour.Database.Economy.EcoAccount account)
    {
        if (account is null)
            return null;
        
        return new EcoAccount()
        {
            Id = account.Id,
            AccountType = account.AccountType,
            UserId = account.UserId,
            PlanetId = account.PlanetId,
            CurrencyId = account.CurrencyId,
            BalanceValue = account.BalanceValue
        };
    }
    
    public static Valour.Database.Economy.EcoAccount ToDatabase(this EcoAccount account)
    {
        if (account is null)
            return null;
        
        return new Valour.Database.Economy.EcoAccount()
        {
            Id = account.Id,
            AccountType = account.AccountType,
            UserId = account.UserId,
            PlanetId = account.PlanetId,
            CurrencyId = account.CurrencyId,
            BalanceValue = account.BalanceValue
        };
    }
}