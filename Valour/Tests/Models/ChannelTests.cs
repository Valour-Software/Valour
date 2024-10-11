using Valour.Shared.Models;

namespace Valour.Tests.Models;

public class ChannelTests
{
    [Fact]
    public void TestChannelDepth()
    {
        var channel = new Server.Models.Channel();
        
        // Depth checks
        
        // Depth 0 (top level)
        channel.Position = 0x01_00_00_00;
        Assert.Equal(0, channel.Depth);
        
        // Depth 1
        channel.Position = 0x01_01_00_00;
        Assert.Equal(1, channel.Depth);
        
        // Depth 2
        channel.Position = 0x01_01_01_00;
        Assert.Equal(2, channel.Depth);
        
        // Depth 3
        channel.Position = 0x01_01_01_01;
        Assert.Equal(3, channel.Depth);
        
        // Local position checks
        channel.Position = 0x01_00_00_00;
        Assert.Equal(1, channel.LocalPosition);
        
        channel.Position = 0x01_01_00_00;
        Assert.Equal(1, channel.LocalPosition);
        
        channel.Position = 0x01_02_00_00;
        Assert.Equal(2, channel.LocalPosition);
        
        channel.Position = 0x01_01_01_05;
        Assert.Equal(5, channel.LocalPosition);
    }

    [Fact]
    public void TestChannelPositionOperations1()
    {
        var parentPosition = 0x01_01_00_00;
        var relativePosition = 0x02;
        
        var appended = ISharedChannel.AppendRelativePosition(parentPosition, relativePosition);
        
        Assert.Equal(0x01_01_02_00, appended);
        
        var depth = ISharedChannel.GetDepth(appended);
        
        Assert.Equal(2, depth);
        
        var localPosition = ISharedChannel.GetLocalPosition(appended);
        
        Assert.Equal(2, localPosition);
    }
    
    [Fact]
    public void TestChannelPositionOperations2()
    {
        var parentPosition = 0x01_01_01_00;
        var relativePosition = 0xA0;
        
        var appended = ISharedChannel.AppendRelativePosition(parentPosition, relativePosition);
        
        Assert.Equal(0x01_01_01_A0, appended);
        
        var depth = ISharedChannel.GetDepth(appended);
        
        Assert.Equal(3, depth);
        
        var localPosition = ISharedChannel.GetLocalPosition(appended);
        
        Assert.Equal(0xA0, localPosition);
    }
}