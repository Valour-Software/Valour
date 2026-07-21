using Valour.Client.Components.Sidebar.Directory;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Tests.Client;

public class ChannelDragManagerTests
{
    [Fact]
    public async Task MoveChannelAsync_RejectsSelfDrop()
    {
        var manager = new ChannelDragManager();
        var channel = CreateChannel(1, ChannelPosition.AppendRelativePosition(0, 1));

        var result = await manager.MoveChannelAsync(
            channel,
            channel,
            placeBefore: false,
            insideCategory: false);

        Assert.False(result.Success);
        Assert.Contains("itself", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MoveChannelAsync_RejectsMovingParentIntoDescendant()
    {
        var manager = new ChannelDragManager();
        var parentPosition = ChannelPosition.AppendRelativePosition(0, 1);
        var parent = CreateChannel(1, parentPosition, ChannelTypeEnum.PlanetCategory);
        var descendant = CreateChannel(
            2,
            ChannelPosition.AppendRelativePosition(parentPosition, 1),
            ChannelTypeEnum.PlanetCategory);

        var result = await manager.MoveChannelAsync(
            parent,
            descendant,
            placeBefore: false,
            insideCategory: true);

        Assert.False(result.Success);
        Assert.Contains("loop", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Channel CreateChannel(
        long id,
        uint rawPosition,
        ChannelTypeEnum type = ChannelTypeEnum.PlanetChat) => new(new ValourClient("https://valour.test/"))
    {
        Id = id,
        PlanetId = 10,
        Name = $"Channel {id}",
        Description = $"Channel {id}",
        ChannelType = type,
        RawPosition = rawPosition
    };
}
