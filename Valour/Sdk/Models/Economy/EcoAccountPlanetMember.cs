using Valour.Sdk.ModelLogic;

namespace Valour.Sdk.Models.Economy;

public class EcoAccountPlanetMember : ClientModel<EcoAccountPlanetMember>
{
    public EcoAccount Account { get; set; }
    public PlanetMember Member { get; set; }
    
    public override EcoAccountPlanetMember AddToCache()
    {
        Account = Account.AddToCache();
        Member = Member.AddToCache();
        
        return this;
    }

    public override EcoAccountPlanetMember TakeAndRemoveFromCache()
    {
        Account = Account.TakeAndRemoveFromCache();
        Member = Member.TakeAndRemoveFromCache();
        
        return this;
    }
}