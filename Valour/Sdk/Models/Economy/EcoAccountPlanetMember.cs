using Valour.Sdk.ModelLogic;

namespace Valour.Sdk.Models.Economy;

public class EcoAccountPlanetMember : ClientModel<EcoAccountPlanetMember>
{
    public string RenderId => Guid.NewGuid().ToString();
    
    public EcoAccount Account { get; set; }
    public PlanetMember Member { get; set; }

    public override void SyncSubModels(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        Account = Account.Sync(Client, flags);
        Member = Member.Sync(Client, flags);
    }

    public override EcoAccountPlanetMember AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }

    public override EcoAccountPlanetMember RemoveFromCache(bool skipEvents = false)
    {
        return this;
    }
}