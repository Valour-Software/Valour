using System.Collections.Concurrent;
using Valour.Shared;

namespace Valour.Sdk.Nodes;

public static class LazyGetRequestCache<T>
{
    public static ConcurrentDictionary<string, Lazy<Task<TaskResult<T>>>> Cache { get; private set; } 
        = new();
}