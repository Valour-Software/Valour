using Valour.Sdk.Client;
using Valour.Sdk.Models.Economy;

namespace Valour.Tests.Client;

public class EcoServiceTests
{
    [Fact]
    public void ApplySelfGlobalAccount_ExposesBootstrapBalance()
    {
        var client = new ValourClient("https://api.valour.example/");
        var account = new EcoAccount(client)
        {
            Id = 42,
            BalanceValue = 123.45m,
        };

        client.EcoService.ApplySelfGlobalAccount(account);

        Assert.Same(account, client.EcoService.SelfGlobalAccount);
        Assert.Equal(123.45m, client.EcoService.SelfGlobalAccount.BalanceValue);
    }
}
