using Valour.Shared.Models;

namespace Valour.Sdk.ModelLogic;

public class ModelQueryResponse<T> : QueryResponse<T>
    where T : ClientModel<T>
{
    public void Sync()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i] = Items[i].Node.Client.Cache.Sync(Items[i]);
        }
    }
}