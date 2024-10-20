using Valour.Shared.Models;

namespace Valour.Sdk.ModelLogic;

public interface ISortableModel<TModel, TId> : ISortable
    where TModel : ClientModel<TModel, TId>
    where TId : IEquatable<TId>
{
}