using Valour.Sdk.Client;
using Valour.Shared.Models;

namespace Valour.Sdk.ModelLogic;

public class ModelQueryResponse<T> : QueryResponse<T>
    where T : ClientModel<T>
{
    public void Sync(ValourClient client)
    {
        Items.SyncAll(client);
    }
}