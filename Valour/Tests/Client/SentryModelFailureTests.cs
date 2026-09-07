using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class SentryModelFailureTests
{
    [Fact]
    public async Task UnsavedModel_UpdateAndDeleteReturnActionableFailures()
    {
        var model = new PlanetRole(new ValourClient("https://api.valour.example/"));
        Assert.False((await model.UpdateAsync()).Success);
        Assert.False((await model.DeleteAsync()).Success);
    }

    [Fact]
    public async Task ModelWithoutClientOrNode_ReturnsFailureInsteadOfThrowing()
    {
        var model = new PlanetRole(null);
        Assert.False((await model.CreateAsync()).Success);
        model.Id = 123;
        Assert.False((await model.CreateAsync()).Success);
        Assert.False((await model.UpdateAsync()).Success);
        Assert.False((await model.DeleteAsync()).Success);
    }
}
