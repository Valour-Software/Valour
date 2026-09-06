using Valour.Shared.Villages;

namespace Valour.Tests.Services;

public class VillageWallTopologyTests
{
    [Fact]
    public void EveryRawNeighborMask_ResolvesToOneOfThe47AuthoredFrames()
    {
        var frames = Enumerable.Range(0, 256)
            .Select(VillageWallTopology.ResolveFrame)
            .ToHashSet();

        Assert.Equal(47, frames.Count);
        Assert.Equal(Enumerable.Range(0, 47), frames.Order());
    }

    [Theory]
    [InlineData(0, 46)]
    [InlineData(255, 0)]
    [InlineData(VillageWallTopology.North, 44)]
    [InlineData(VillageWallTopology.East, 43)]
    [InlineData(VillageWallTopology.South, 42)]
    [InlineData(VillageWallTopology.West, 45)]
    public void ResolveFrame_MatchesTheAuthoredEightBySixLayout(int mask, int frame)
    {
        Assert.Equal(frame, VillageWallTopology.ResolveFrame(mask));
    }

    [Fact]
    public void OrphanedDiagonal_IsIgnoredUntilItsCardinalNeighborsExist()
    {
        Assert.Equal(
            VillageWallTopology.ResolveFrame(0),
            VillageWallTopology.ResolveFrame(VillageWallTopology.NorthEast));
        Assert.NotEqual(
            VillageWallTopology.ResolveFrame(VillageWallTopology.North | VillageWallTopology.East),
            VillageWallTopology.ResolveFrame(
                VillageWallTopology.North |
                VillageWallTopology.NorthEast |
                VillageWallTopology.East));
    }

    [Theory]
    [InlineData("wall:modern.green:0", "modern.green", 0)]
    [InlineData("wall:modern-green_2:46", "modern-green_2", 46)]
    public void DefinitionKey_RoundTrips(string key, string expectedSet, int expectedFrame)
    {
        Assert.True(VillageWallTopology.TryParseDefinitionKey(key, out var wallSet, out var frame));
        Assert.Equal(expectedSet, wallSet);
        Assert.Equal(expectedFrame, frame);
        Assert.Equal(key, VillageWallTopology.MakeDefinitionKey(wallSet, frame));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wall::0")]
    [InlineData("wall:unsafe/asset:0")]
    [InlineData("wall:modern:47")]
    [InlineData("wall:modern:-1")]
    [InlineData("furniture:modern:0")]
    public void DefinitionKey_RejectsMalformedOrReservedValues(string? key)
    {
        Assert.False(VillageWallTopology.TryParseDefinitionKey(key, out _, out _));
    }
}
