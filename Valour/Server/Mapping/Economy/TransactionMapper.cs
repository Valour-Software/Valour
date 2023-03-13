namespace Valour.Server.Mapping.Economy;

public static class TransactionMapper
{
    public static Transaction ToModel(this Valour.Database.Economy.Transaction transaction)
    {
        if (transaction is null)
            return null;
        
        return new Transaction()
        {
            Id = transaction.Id,
            PlanetId = transaction.PlanetId,
            UserFromId = transaction.UserFromId,
            AccountFromId = transaction.AccountFromId,
            UserToId = transaction.UserToId,
            AccountToId = transaction.AccountToId,
            TimeStamp = transaction.TimeStamp,
            Description = transaction.Description,
            Data = transaction.Data,
            Fingerprint = transaction.Fingerprint,
            ForcedBy = transaction.ForcedBy,
        };
    }
    
    public static Valour.Database.Economy.Transaction ToDatabase(this Transaction transaction)
    {
        if (transaction is null)
            return null;
        
        return new Valour.Database.Economy.Transaction()
        {
            Id = transaction.Id,
            PlanetId = transaction.PlanetId,
            UserFromId = transaction.UserFromId,
            AccountFromId = transaction.AccountFromId,
            UserToId = transaction.UserToId,
            AccountToId = transaction.AccountToId,
            TimeStamp = transaction.TimeStamp,
            Description = transaction.Description,
            Data = transaction.Data,
            Fingerprint = transaction.Fingerprint,
            ForcedBy = transaction.ForcedBy,
        };
    }
}