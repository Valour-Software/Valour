using Valour.Shared.Models;

namespace Valour.Tests.Models;

public class ChannelTests
{
    [Fact]
    public void TestChannelDepth()
    {
        // Depth checks
        var pos = new ChannelPosition(0x00_00_00_00);
        
        // Depth 0 (planet)
        Assert.Equal(0u, pos.Depth);
        
        // Depth 1
        pos = new ChannelPosition(0x01_00_00_00);
        Assert.Equal(1u, pos.Depth);
        
        // Depth 2
        pos = new ChannelPosition(0x01_01_00_00);
        Assert.Equal(2u, pos.Depth);
        
        // Depth 3
        pos = new ChannelPosition(0x01_01_01_00);
        Assert.Equal(3u, pos.Depth);
        
        // Depth 4
        pos = new ChannelPosition(0x01_01_01_01);
        Assert.Equal(4u, pos.Depth);
        
        // Local position checks
        pos = new ChannelPosition(0x01_00_00_00);
        Assert.Equal(1u, pos.LocalPosition);
        
        pos = new ChannelPosition(0x01_01_00_00);
        Assert.Equal(1u, pos.LocalPosition);
        
        pos = new ChannelPosition(0x01_02_00_00);
        Assert.Equal(2u, pos.LocalPosition);
        
        pos = new ChannelPosition(0x01_01_01_05);
        Assert.Equal(5u, pos.LocalPosition);
    }

    [Fact]
    public void TestChannelPositionOperations1()
    {
        var parentPosition = new ChannelPosition(0x01_01_00_00u);
        var relativePosition = 0x02u;

        var appended = parentPosition.Append(relativePosition);

        Assert.Equal(0x01_01_02_00u, appended.RawPosition);
        
        Assert.Equal(3u, appended.Depth);

        Assert.Equal(2u, appended.LocalPosition);
    }
    
    [Fact]
    public void TestChannelPositionOperations2()
    {
        var parentPosition = new ChannelPosition(0x01_01_01_00u);
        var relativePosition = 0xA0u;
        
        var appended = parentPosition.Append(relativePosition);
        
        Assert.Equal(0x01_01_01_A0u, appended.RawPosition);
        
        Assert.Equal(4u, appended.Depth);
        
        Assert.Equal(0xA0u, appended.LocalPosition);
    }
}