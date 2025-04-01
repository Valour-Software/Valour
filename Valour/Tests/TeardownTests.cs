namespace Valour.Tests;


[CollectionDefinition("TeardownCollection", DisableParallelization = true)]
public class TeardownCollectionDefinition
{
    
}

[Collection("TeardownCollection")]
public class TeardownTests : IClassFixture<TeardownTestFixture>
{
    private readonly TeardownTestFixture _fixture;
    
    public TeardownTests(TeardownTestFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public async Task DeleteUser()
    {
        var client = _fixture.Client;
        var details = TestShared.TestUserDetails;
        var result = await client.DeleteMyAccountAsync(details.Password);
        
        Assert.True(result.Success);
    }
}