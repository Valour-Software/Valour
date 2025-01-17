using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")] // We want access to the test user
public class PlanetPermissionsServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _httpClient;
    private ValourClient _client;
    private RegisterUserRequest _testUserDetails;
    private LoginTestFixture _fixture;
    private WebApplicationFactory<Program> _factory;

    public PlanetPermissionsServiceTests(LoginTestFixture fixture)
    {
        _client = fixture.Client;
        _httpClient = _client.Http;
        _fixture = fixture;
        _testUserDetails = fixture.TestUserDetails;
        _factory = fixture.Factory;
    }

    [Fact]
    public async Task GetValourCentralTest()
    {
        var scope = _factory.Services.CreateScope();
        var planetService = scope.ServiceProvider.GetRequiredService<PlanetService>();
        var valourCentral = await planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(valourCentral);
    }

    [Fact]
    public async Task OwnerPermissions()
    {
        var scope = _factory.Services.CreateScope();
        var planetService = scope.ServiceProvider.GetRequiredService<PlanetService>();
        var planetMemberService = scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        var channelService = scope.ServiceProvider.GetRequiredService<ChannelService>();
        
        var valourCentral = await planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(valourCentral);

        var ownerMember = await planetMemberService.GetByUserAsync(valourCentral.OwnerId, ISharedPlanet.ValourCentralId);
        Assert.NotNull(ownerMember);
        
        // Ensure owner has access to all channels
        var allChannels = await planetService.GetAllChannelsAsync(ISharedPlanet.ValourCentralId);
        Assert.NotEmpty(allChannels);

        var channelAccess = await planetService.GetMemberChannelsAsync(ownerMember);

        foreach (var channel in allChannels)
        {
            var canAccess = channelAccess.Contains(channel.Id);
            Assert.True(canAccess);

            var permissions = await channelService.GetPermissionsAsync(channel, ownerMember, channel.ChannelType);
            Assert.Equal(Permission.FULL_CONTROL, permissions);
        }

        var canManage = await planetMemberService.HasPermissionAsync(ownerMember, PlanetPermissions.Manage);
        Assert.True(canManage);
    }

    [Fact]
    public async Task MemberPermissions()
    {
        var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var planetService = scope.ServiceProvider.GetRequiredService<PlanetService>();
        var planetMemberService = scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        var channelService = scope.ServiceProvider.GetRequiredService<ChannelService>();
        var roleService = scope.ServiceProvider.GetRequiredService<PlanetRoleService>();
        
        var valourCentral = await planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(valourCentral);

        var member = await planetMemberService.GetByUserAsync(_client.Me.Id, ISharedPlanet.ValourCentralId);
        Assert.NotNull(member); // New member should be a member of valour central

        var oldRoleKey = member.RoleHashKey;
        Assert.NotEqual(0, oldRoleKey); // Should have a role key
        
        // Ensure they do NOT have planet permissions
        var canManage = await planetMemberService.HasPermissionAsync(member, PlanetPermissions.Manage);
        Assert.False(canManage);
        
        // Create a new role with admin permissions
        var createAdminRoleResult = await roleService.CreateAsync(new PlanetRole()
        {
            Name = "Test Admin",
            IsAdmin = true,
            PlanetId = valourCentral.Id,
        });
        
        Assert.True(createAdminRoleResult.Success);
        Assert.NotNull(createAdminRoleResult.Data);

        var adminRole = createAdminRoleResult.Data;
        
        // Ensure the member still can't manage the planet. We haven't assigned the role yet.
        canManage = await planetMemberService.HasPermissionAsync(member, PlanetPermissions.Manage);
        Assert.False(canManage);
        
        // Assign the role to the member
        var assignRoleResult = await planetMemberService.AddRoleAsync(member.Id, adminRole.Id);
        Assert.True(assignRoleResult.Success);

        // We have to fetch the member again to get the updated role key since it's a model and not a db object
        member = await planetMemberService.GetByUserAsync(_client.Me.Id, ISharedPlanet.ValourCentralId);
        
        // Should have a new role key on the member
        Assert.NotEqual(oldRoleKey, member.RoleHashKey);
        
        // Ensure the member now has manage permissions
        canManage = await planetMemberService.HasPermissionAsync(member, PlanetPermissions.Manage);
        Assert.True(canManage);
    }
}