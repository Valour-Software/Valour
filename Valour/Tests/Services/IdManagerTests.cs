using Valour.Server.Database;

namespace Valour.Tests.Services;

public class IdManagerTests
{
    [Fact]
    public void BulkVillageCreation_GeneratesUniqueIdsAcrossSequenceBoundaries()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();
        Parallel.For(0, 4096, _ => ids.Add(IdManager.Generate()));
        Assert.Equal(4096, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id > 0));
    }
}
