namespace Valour.Shared.Models;

public interface ISharedModel<TId>
{
    TId Id { get; set; }
}


