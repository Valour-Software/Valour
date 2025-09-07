using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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
    
    // Services
    private readonly PlanetPermissionService _permissionService;
    private readonly PlanetService _planetService;
    private readonly ChannelService _channelService;
    private readonly PlanetMemberService _planetMemberService;
    private readonly PlanetRoleService _roleService;
    private readonly UserService _userService;
    private readonly PermissionsNodeService _nodeService;
    private readonly ValourDb _db;

    public PlanetPermissionsServiceTests(LoginTestFixture fixture)
    {
        _client = fixture.Client;
        _httpClient = _client.Http;
        _fixture = fixture;
        _testUserDetails = fixture.PrimaryTestUserDetails;
        _factory = fixture.Factory;
        
        var scope = _factory.Services.CreateScope();
        _db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        _permissionService = scope.ServiceProvider.GetRequiredService<PlanetPermissionService>();
        _planetService = scope.ServiceProvider.GetRequiredService<PlanetService>();
        _channelService = scope.ServiceProvider.GetRequiredService<ChannelService>();
        _planetMemberService = scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        _roleService = scope.ServiceProvider.GetRequiredService<PlanetRoleService>();
        _userService = scope.ServiceProvider.GetRequiredService<UserService>();
        _nodeService = scope.ServiceProvider.GetRequiredService<PermissionsNodeService>();
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
    public async Task GetAllRoleCombosWithRole()
    {
        // TODO: Come up with this test, it's important (sob)
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
        await TestHasPermissionAllChannels(ownerMember);

        var canManage = await planetMemberService.HasPermissionAsync(ownerMember.Id, PlanetPermissions.Manage);
        Assert.True(canManage);
    }
    
    private async Task TestHasPermissionAllChannels(PlanetMember member)
    {
        var allChannels = await _planetService.GetAllChannelsAsync(member.PlanetId);
        Assert.NotEmpty(allChannels);

        var channelAccess = await _planetService.GetMemberChannelsAsync(member.Id);

        foreach (var channel in allChannels)
        {
            var canAccess = channelAccess?.Contains(channel.Id) ?? false;
            Assert.True(canAccess);

            var permissions = await _channelService.GetPermissionsAsync(channel, member, channel.ChannelType);
            Assert.Equal(Permission.FULL_CONTROL, permissions);
        }
    }

    [Fact]
    public async Task MemberPermissions()
    {
        var valourCentral = await _planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(valourCentral);

        var member = await _planetMemberService.GetByUserAsync(_client.Me.Id, ISharedPlanet.ValourCentralId);
        Assert.NotNull(member); // New member should be a member of valour central

        var oldRoleKey = member.RoleMembership;
        Assert.NotEqual(new PlanetRoleMembership(), oldRoleKey); // Should have membership
        
        // Ensure they do NOT have planet permissions
        var canManage = await _planetMemberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage);
        Assert.False(canManage, "Member should not have manage permissions");
        
        // Create a new role with admin permissions
        var createAdminRoleResult = await _roleService.CreateAsync(new PlanetRole()
        {
            Name = "Test Admin",
            IsAdmin = true,
            PlanetId = valourCentral.Id,
        });
        
        Assert.True(createAdminRoleResult.Success, "Failed to create admin role");
        Assert.NotNull(createAdminRoleResult.Data);
        
        var adminRole = createAdminRoleResult.Data;

        var defaultRole = await _planetService.GetDefaultRole(adminRole.PlanetId);
        
        Assert.NotNull(defaultRole);

        // Create a channel only admins can access
        var adminChannelCreateResult = await _channelService.CreateAsync(new Channel()
        {
            Name = "Admin Channel",
            PlanetId = valourCentral.Id,
            ParentId = null,
            ChannelType = ChannelTypeEnum.PlanetChat,
            Description = "Admins only",
            InheritsPerms = false,
            RawPosition = 0
        }, new List<PermissionsNode>()
        {
            new PermissionsNode() // Sets no access for default
            {
                PlanetId = defaultRole.PlanetId,
                RoleId = defaultRole.Id,
                Mask = Permission.FULL_CONTROL, // Enable all bits
                Code = 0, // No permissions
            },
            new PermissionsNode()
            {
                PlanetId = adminRole.PlanetId,
                RoleId = adminRole.Id,
                Mask = Permission.FULL_CONTROL, // Enable all bits
                Code = Permission.FULL_CONTROL, // Full control
            }
        });
        
        // Make sure permission nodes were created
        var nodes = await _roleService.GetNodesAsync(adminRole.Id);
        Assert.NotEmpty(nodes);
        Assert.Equal(Permission.FULL_CONTROL, nodes[0].Mask);
        Assert.Equal(Permission.FULL_CONTROL, nodes[0].Code);
        
        Assert.True(adminChannelCreateResult.Success, "Failed to create admin channel: " + adminChannelCreateResult.Message);
        Assert.NotNull(adminChannelCreateResult.Data);
        
        var adminChannel = adminChannelCreateResult.Data;
        
        // Ensure the member does not have access to the admin channel
        var channelAccess = await _planetService.GetMemberChannelsAsync(member.Id);
        var canAccess = channelAccess?.Contains(adminChannel.Id) ?? false;
         Assert.False(canAccess, "Member should not have access to admin channel yet");
        
        // Ensure the member still can't manage the planet. We haven't assigned the role yet.
        canManage = await _planetMemberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage);
        Assert.False(canManage, "Member should not have manage permissions");
        
        // Assign the role to the member
        var assignRoleResult = await _planetMemberService.AddRoleAsync(member.PlanetId, member.Id, adminRole.Id);
        Assert.True(assignRoleResult.Success);
        
        // Update member
        member = await _planetMemberService.GetAsync(member.Id);
        
        // Should have a new role key on the member
        Assert.NotEqual(oldRoleKey, member.RoleMembership);
        
        // Ensure the member now has manage permissions
        canManage = await _planetMemberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage);
        Assert.True(canManage);
        
        // Ensure admin has access to all channels
        await TestHasPermissionAllChannels(member);
        
        // Remove the admin role
        var removeRoleResult = await _planetMemberService.RemoveRoleAsync(member.PlanetId, member.Id, adminRole.Id);
        Assert.True(removeRoleResult.Success);
        
        // Ensure the member no longer has manage permissions
        canManage = await _planetMemberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage);
        Assert.False(canManage);
        
        adminRole.IsAdmin = false;
        adminRole.Name = "Not Admin";
        
        // Remove admin from the admin role and make it a normal role
        var removeAdminResult = await _roleService.UpdateAsync(adminRole);
        Assert.True(removeAdminResult.Success, "Failed to update role");
        
        // Give the admin role back to the member
        assignRoleResult = await _planetMemberService.AddRoleAsync(member.PlanetId, member.Id, adminRole.Id);
        Assert.True(assignRoleResult.Success, "Failed to assign role");
        
        // Member should not have planet management permissions
        canManage = await _planetMemberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage);
        Assert.False(canManage, "Member should not have manage permissions");
        
        // Ensure the member still has access to the admin channel (permission node still grants it)
        channelAccess = await _planetService.GetMemberChannelsAsync(member.Id);
        canAccess = channelAccess?.Contains(adminChannel.Id) ?? false;
        
        Assert.True(canAccess, "Member should have access to admin channel");
        
        // Update permissions node to remove access
        var node = nodes[0];
        node.Code = 0;

        var updateNodeResult = await _nodeService.PutAsync(node);
        Assert.True(updateNodeResult.Success, "Failed to update node");
        
        // Ensure the member no longer has access to the admin channel
        channelAccess = await _planetService.GetMemberChannelsAsync(member.Id);
        canAccess = channelAccess?.Contains(adminChannel.Id) ?? false;
        
        Assert.False(canAccess, "Member should not have access to admin channel");
        
        // Delete the admin role
        var deleteRoleResult = await _roleService.DeleteAsync(adminRole.PlanetId, adminRole.Id);
        Assert.True(deleteRoleResult.Success, "Failed to delete role");
        
        // Delete the test channel
        var deleteChannelResult = await _channelService.DeletePlanetChannelAsync(adminChannel.PlanetId!.Value, adminChannel.Id);
        Assert.True(deleteChannelResult.Success, "Failed to delete channel");
        
        // Should still have no access to the channel
        channelAccess = await _planetService.GetMemberChannelsAsync(member.Id);
        canAccess = channelAccess?.Contains(adminChannel.Id) ?? false;
        
        Assert.False(canAccess, "Member should not have access to admin channel");
    }
}