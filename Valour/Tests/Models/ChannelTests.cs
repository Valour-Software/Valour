namespace Valour.Tests.Models;

public class ChannelTests
{
    [Fact]
    public void TestChannelDepth()
    {
        var channel = new Server.Models.Channel();
        
        // Depth checks
        
        // Depth 1 (top level)
        channel.Position = 0x01_00_00_00;
        Assert.Equal(1, channel.Depth);
        
        // Depth 2
        channel.Position = 0x01_01_00_00;
        Assert.Equal(2, channel.Depth);
        
        // Depth 3
        channel.Position = 0x01_01_01_00;
        Assert.Equal(3, channel.Depth);
        
        // Depth 4
        channel.Position = 0x01_01_01_01;
        Assert.Equal(4, channel.Depth);
        
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
}