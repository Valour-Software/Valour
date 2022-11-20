using Valour.Shared.Items;

namespace Valour.Api.Items.Channels;
public interface IChannel : ISharedItem
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Task Open();
    public Task Close();
    public Task SendIsTyping();
}
