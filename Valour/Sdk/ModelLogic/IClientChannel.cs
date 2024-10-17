using Valour.Shared.Models;

namespace Valour.Sdk.ModelLogic;

public interface IClientChannel : ISharedModel<long>
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Task Open(string key);
    public Task Close(string key);
}
