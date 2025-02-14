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

    public override EcoAccountPlanetMember RemoveFromCache()
    {
        Account = Account.RemoveFromCache();
        Member = Member.RemoveFromCache();
        
        return this;
    }
}