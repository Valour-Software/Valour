using Microsoft.Extensions.ObjectPool;
using Valour.Shared.Models;

namespace Valour.Server.Utilities;

public class PooledServerModelListPolicy<TModel, TId> : PooledObjectPolicy<ServerModelList<TModel, TId>>
    where TModel : ServerModel<TId>
    where TId : IEquatable<TId>
{
    public override ServerModelList<TModel, TId> Create()
    {
        return new ServerModelList<TModel, TId>();
    }

    public override bool Return(ServerModelList<TModel, TId> obj)
    {
        // Clear the list and dictionary
        obj.Clear();
        return true;
    }
}

public class PooledSortedServerModelListPolicy<TModel, TId> : PooledObjectPolicy<SortedServerModelList<TModel, TId>>
    where TModel : ServerModel<TId>, ISortable
    where TId : IEquatable<TId>
{
    public override SortedServerModelList<TModel, TId> Create()
    {
        return new SortedServerModelList<TModel, TId>();
    }

    public override bool Return(SortedServerModelList<TModel, TId> obj)
    {
        // Clear the list and dictionary
        obj.Clear();
        return true;
    }
}