using Xunit.Abstractions;

[assembly: TestCollectionOrderer("Valour.Tests.CustomCollectionOrderer", "Valour.Tests")]

namespace Valour.Tests;

public class CustomCollectionOrderer : ITestCollectionOrderer
{
    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections)
    {
        // 1. Convert to a list for multiple passes
        var allCollections = testCollections.ToList();

        // 2. Extract the "last" collection(s)
        var lastCollections = allCollections
            .Where(c => c.DisplayName.Contains("TeardownCollection"))
            .ToList();

        // 3. The rest of the collections (which can run in parallel)
        var otherCollections = allCollections
            .Where(c => !c.DisplayName.Contains("TeardownCollection"))
            .ToList();

        // Return "other" collections first, then last collection(s)
        // xUnit will still attempt to run them in parallel if possible,
        // but it will queue them in this order.
        return otherCollections.Concat(lastCollections);
    }
}
