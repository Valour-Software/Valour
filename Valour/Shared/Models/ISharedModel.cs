namespace Valour.Shared.Models;

public interface ISharedModel
{
    public object GetId();
}

public interface ISharedModel<TId> : ISharedModel
{
    TId Id { get; set; }
    object ISharedModel.GetId()
    {
        return Id;
    }
}


