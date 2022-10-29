using Microsoft.Extensions.Caching.Memory;

namespace Valour.Server.Cdn;

public class CdnMemoryCache
{
    public MemoryCache Cache { get; } = new MemoryCache(
        new MemoryCacheOptions() { 
            SizeLimit = 500000000
        });
}
