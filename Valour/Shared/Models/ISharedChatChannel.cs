namespace Valour.Shared.Models;

public interface ISharedChatChannel : ISharedChannel
{
    long MessageCount { get; set; }
}
