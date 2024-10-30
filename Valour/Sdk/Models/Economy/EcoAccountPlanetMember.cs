using Valour.Sdk.ModelLogic;

namespace Valour.Sdk.Models.Economy;

public class EcoAccountPlanetMember : ClientModel<EcoAccountPlanetMember>
{
    public EcoAccount Account { get; set; }
    public PlanetMember Member { get; set; }
    
    public override EcoAccountPlanetMember AddToCacheOrReturnExisting()
    {
        Account = Account.AddToCacheOrReturnExisting();
        Member = Member.AddToCacheOrReturnExisting();
        
        return this;
    }

    public override EcoAccountPlanetMember TakeAndRemoveFromCache()
    {
        Account = Account.TakeAndRemoveFromCache();
        Member = Member.TakeAndRemoveFromCache();
        
        return this;
    }
}