using Valour.Server.Email;

namespace Valour.Tests.Server;

public class EmailManagerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fake-value")]
    public void CreateClient_ReturnsNullWhenEmailIsDisabled(string? apiKey)
    {
        Assert.Null(EmailManager.CreateClient(apiKey));
    }

    [Fact]
    public void CreateClient_ReturnsClientForConfiguredKey()
    {
        Assert.NotNull(EmailManager.CreateClient("SG.test-key"));
    }
}
