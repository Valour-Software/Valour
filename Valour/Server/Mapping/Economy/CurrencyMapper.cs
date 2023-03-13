namespace Valour.Server.Mapping.Economy;

public static class CurrencyMapper
{
    public static Currency ToModel(this Valour.Database.Economy.Currency currency)
    {
        if (currency is null)
            return null;

        return new Currency()
        {
            Id = currency.Id,
            PlanetId = currency.PlanetId,
            Name = currency.Name,
            PluralName = currency.PluralName,
            ShortCode = currency.ShortCode,
            Symbol = currency.Symbol,
            Issued = currency.Issued,
            DecimalPlaces = currency.DecimalPlaces
        };
    }
    
    public static Valour.Database.Economy.Currency ToDatabase(this Currency currency)
    {
        if (currency is null)
            return null;

        return new Valour.Database.Economy.Currency()
        {
            Id = currency.Id,
            PlanetId = currency.PlanetId,
            Name = currency.Name,
            PluralName = currency.PluralName,
            ShortCode = currency.ShortCode,
            Symbol = currency.Symbol,
            Issued = currency.Issued,
            DecimalPlaces = currency.DecimalPlaces
        };
    }
}