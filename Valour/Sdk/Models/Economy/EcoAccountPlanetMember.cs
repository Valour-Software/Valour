using Valour.Sdk.ModelLogic;

namespace Valour.Sdk.Models.Economy;

public class EcoAccountPlanetMember : ClientModel<EcoAccountPlanetMember>
{
    public EcoAccount Account { get; set; }
    public PlanetMember Member { get; set; }
    
    public override EcoAccountPlanetMember AddToCache(bool skipEvents = false, bool batched = false)
    {
        Account = Account.AddToCache(skipEvents, batched);
        Member = Member.AddToCache(skipEvents, batched);
        
        return this;
    }

    public override EcoAccountPlanetMember RemoveFromCache(bool skipEvents = false)
    {
        Account = Account.RemoveFromCache(skipEvents);
        Member = Member.RemoveFromCache(skipEvents);
        
        return this;
    }
}