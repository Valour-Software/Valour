using Valour.Shared.Models;

namespace Valour.Shared.Models;

public class MessageTransferData<T> where T : ISharedMessage
{
    public T Message { get; set; }
    public T Reply { get; set; }
}