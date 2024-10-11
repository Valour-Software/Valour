using Valour.Shared.Models;

namespace Valour.Sdk.Models;
public interface IChannel : ISharedModel
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Task Open(string key);
    public Task Close(string key);
}
