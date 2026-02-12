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
        var planet = await _planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(planet);

        var member = await _planetMemberService.GetByUserAsync(_client.Me.Id, planet.Id);
        Assert.NotNull(member);

        PlanetRole? createdRole = null;
        try
        {
            var createRoleResult = await _roleService.CreateAsync(new PlanetRole()
            {
                Name = $"combo-role-{Guid.NewGuid():N}".Substring(0, 20),
                PlanetId = planet.Id,
                Permissions = 0,
                ChatPermissions = 0,
                CategoryPermissions = 0,
                VoicePermissions = 0
            });

            Assert.True(createRoleResult.Success, createRoleResult.Message);
            Assert.NotNull(createRoleResult.Data);
            createdRole = createRoleResult.Data;

            var addRoleResult = await _planetMemberService.AddRoleAsync(planet.Id, member.Id, createdRole.Id);
            Assert.True(addRoleResult.Success, addRoleResult.Message);

            var combos = await _permissionService.GetAllUniqueRoleCombinationsForPlanet(planet.Id);
            Assert.NotEmpty(combos);
            Assert.Contains(combos, combo => combo.HasRole(createdRole.FlagBitIndex));
        }
        finally
        {
            if (createdRole is not null)
            {
                await _planetMemberService.RemoveRoleAsync(planet.Id, member.Id, createdRole.Id);
                await _roleService.DeleteAsync(planet.Id, createdRole.Id);
            }
        }
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

    [Fact]
    public async Task ChannelAccessCacheInvalidatesOnChannelCreate()
    {
        var planet = await _planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(planet);

        var member = await _planetMemberService.GetByUserAsync(_client.Me.Id, planet.Id);
        Assert.NotNull(member);

        // Prime cache for this role membership.
        var before = await _planetService.GetMemberChannelsAsync(member.Id);
        Assert.NotNull(before);

        var channel = new Channel()
        {
            Name = $"cache-invalidate-{Guid.NewGuid():N}".Substring(0, 24),
            PlanetId = planet.Id,
            ParentId = null,
            ChannelType = ChannelTypeEnum.PlanetChat,
            Description = "Regression test channel",
            InheritsPerms = false,
            RawPosition = 0
        };

        Channel? createdChannel = null;
        try
        {
            var createResult = await _channelService.CreateAsync(channel);
            Assert.True(createResult.Success, createResult.Message);
            Assert.NotNull(createResult.Data);
            createdChannel = createResult.Data;

            var after = await _planetService.GetMemberChannelsAsync(member.Id);
            Assert.NotNull(after);
            Assert.True(after.Contains(createdChannel.Id),
                "Newly created channel should be visible immediately after cache invalidation.");
        }
        finally
        {
            if (createdChannel is not null)
            {
                await _channelService.DeletePlanetChannelAsync(planet.Id, createdChannel.Id);
            }
        }
    }

    [Fact]
    public async Task PlanetPermissionCacheInvalidatesOnRolePermissionUpdate()
    {
        var planet = await _planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(planet);

        var member = await _planetMemberService.GetByUserAsync(_client.Me.Id, planet.Id);
        Assert.NotNull(member);

        PlanetRole? role = null;
        try
        {
            var createRoleResult = await _roleService.CreateAsync(new PlanetRole()
            {
                Name = $"perm-update-{Guid.NewGuid():N}".Substring(0, 20),
                PlanetId = planet.Id,
                Permissions = 0,
                ChatPermissions = 0,
                CategoryPermissions = 0,
                VoicePermissions = 0
            });
            Assert.True(createRoleResult.Success, createRoleResult.Message);
            Assert.NotNull(createRoleResult.Data);
            role = createRoleResult.Data;

            var addRoleResult = await _planetMemberService.AddRoleAsync(planet.Id, member.Id, role.Id);
            Assert.True(addRoleResult.Success, addRoleResult.Message);

            // Prime cache in deny state.
            var before = await _planetMemberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage);
            Assert.False(before);

            role.Permissions = PlanetPermissions.Manage.Value;

            var updateRoleResult = await _roleService.UpdateAsync(role);
            Assert.True(updateRoleResult.Success, updateRoleResult.Message);

            var after = await _planetMemberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage);
            Assert.True(after, "Role permission update should invalidate and rebuild planet permission cache.");
        }
        finally
        {
            if (role is not null)
            {
                await _planetMemberService.RemoveRoleAsync(planet.Id, member.Id, role.Id);
                await _roleService.DeleteAsync(planet.Id, role.Id);
            }
        }
    }

    [Fact]
    public async Task RoleDeletionRemovesGrantedPlanetPermission()
    {
        var planet = await _planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(planet);

        var member = await _planetMemberService.GetByUserAsync(_client.Me.Id, planet.Id);
        Assert.NotNull(member);

        PlanetRole? role = null;
        var roleFlagBitIndex = -1;
        try
        {
            var createRoleResult = await _roleService.CreateAsync(new PlanetRole()
            {
                Name = $"perm-delete-{Guid.NewGuid():N}".Substring(0, 20),
                PlanetId = planet.Id,
                Permissions = PlanetPermissions.Manage.Value,
                ChatPermissions = 0,
                CategoryPermissions = 0,
                VoicePermissions = 0
            });
            Assert.True(createRoleResult.Success, createRoleResult.Message);
            Assert.NotNull(createRoleResult.Data);
            role = createRoleResult.Data;
            roleFlagBitIndex = role.FlagBitIndex;

            var addRoleResult = await _planetMemberService.AddRoleAsync(planet.Id, member.Id, role.Id);
            Assert.True(addRoleResult.Success, addRoleResult.Message);

            var hasManageBeforeDelete = await _planetMemberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage);
            Assert.True(hasManageBeforeDelete);

            var deleteRoleResult = await _roleService.DeleteAsync(planet.Id, role.Id);
            Assert.True(deleteRoleResult.Success, deleteRoleResult.Message);

            // Role is now deleted; clear local reference to avoid cleanup calling delete again.
            role = null;

            // Verify with a fresh service scope (same behavior as a new API request),
            // so ExecuteUpdate/ExecuteDelete tracker staleness can't hide regressions.
            using var verifyScope = _factory.Services.CreateScope();
            var verifyMemberService = verifyScope.ServiceProvider.GetRequiredService<PlanetMemberService>();

            var memberAfterDelete = await verifyMemberService.GetAsync(member.Id);
            Assert.False(memberAfterDelete.RoleMembership.HasRole(roleFlagBitIndex));

            var hasManageAfterDelete = await verifyMemberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage);
            Assert.False(hasManageAfterDelete, "Deleting a role should remove its granted planet permissions.");
        }
        finally
        {
            if (role is not null)
            {
                await _planetMemberService.RemoveRoleAsync(planet.Id, member.Id, role.Id);
                await _roleService.DeleteAsync(planet.Id, role.Id);
            }
        }
    }

    [Fact]
    public async Task RemovingRoleRevokesChannelAccessGrantedByRoleNode()
    {
        var planet = await _planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(planet);

        var member = await _planetMemberService.GetByUserAsync(_client.Me.Id, planet.Id);
        Assert.NotNull(member);

        var defaultRole = await _planetService.GetDefaultRole(planet.Id);
        Assert.NotNull(defaultRole);

        PlanetRole? role = null;
        Channel? channel = null;
        try
        {
            var createRoleResult = await _roleService.CreateAsync(new PlanetRole()
            {
                Name = $"channel-role-{Guid.NewGuid():N}".Substring(0, 20),
                PlanetId = planet.Id,
                Permissions = 0,
                ChatPermissions = 0,
                CategoryPermissions = 0,
                VoicePermissions = 0
            });

            Assert.True(createRoleResult.Success, createRoleResult.Message);
            Assert.NotNull(createRoleResult.Data);
            role = createRoleResult.Data;

            var createChannelResult = await _channelService.CreateAsync(new Channel()
            {
                Name = $"role-access-{Guid.NewGuid():N}".Substring(0, 24),
                PlanetId = planet.Id,
                ParentId = null,
                ChannelType = ChannelTypeEnum.PlanetChat,
                Description = "Role node access test",
                InheritsPerms = false,
                RawPosition = 0
            }, new List<PermissionsNode>()
            {
                new PermissionsNode()
                {
                    PlanetId = planet.Id,
                    RoleId = defaultRole.Id,
                    Mask = Permission.FULL_CONTROL,
                    Code = 0
                },
                new PermissionsNode()
                {
                    PlanetId = planet.Id,
                    RoleId = role.Id,
                    Mask = Permission.FULL_CONTROL,
                    Code = Permission.FULL_CONTROL
                }
            });

            Assert.True(createChannelResult.Success, createChannelResult.Message);
            Assert.NotNull(createChannelResult.Data);
            channel = createChannelResult.Data;

            var beforeRole = await _planetService.GetMemberChannelsAsync(member.Id);
            Assert.NotNull(beforeRole);
            Assert.False(beforeRole.Contains(channel.Id));

            var addRoleResult = await _planetMemberService.AddRoleAsync(planet.Id, member.Id, role.Id);
            Assert.True(addRoleResult.Success, addRoleResult.Message);

            var withRole = await _planetService.GetMemberChannelsAsync(member.Id);
            Assert.NotNull(withRole);
            Assert.True(withRole.Contains(channel.Id));

            var removeRoleResult = await _planetMemberService.RemoveRoleAsync(planet.Id, member.Id, role.Id);
            Assert.True(removeRoleResult.Success, removeRoleResult.Message);

            var afterRoleRemoval = await _planetService.GetMemberChannelsAsync(member.Id);
            Assert.NotNull(afterRoleRemoval);
            Assert.False(afterRoleRemoval.Contains(channel.Id),
                "Removing a role should revoke channel access granted exclusively by that role.");
        }
        finally
        {
            if (channel is not null)
            {
                await _channelService.DeletePlanetChannelAsync(planet.Id, channel.Id);
            }

            if (role is not null)
            {
                await _planetMemberService.RemoveRoleAsync(planet.Id, member.Id, role.Id);
                await _roleService.DeleteAsync(planet.Id, role.Id);
            }
        }
    }

    [Fact]
    public async Task ChannelAccessCacheRemainsIsolatedAcrossPlanets()
    {
        var primaryPlanet = await _planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(primaryPlanet);

        var primaryMember = await _planetMemberService.GetByUserAsync(_client.Me.Id, primaryPlanet.Id);
        Assert.NotNull(primaryMember);

        var primaryChannel = await _planetService.GetPrimaryChannelAsync(primaryPlanet.Id);
        Assert.NotNull(primaryChannel);

        var primaryAccessBefore = await _planetService.GetMemberChannelsAsync(primaryMember.Id);
        Assert.NotNull(primaryAccessBefore);
        Assert.True(primaryAccessBefore.Contains(primaryChannel.Id));

        var alternateOwnerId = primaryPlanet.OwnerId != _client.Me.Id
            ? primaryPlanet.OwnerId
            : await _db.Users.AsNoTracking()
                .Where(x => x.Id != _client.Me.Id)
                .Select(x => x.Id)
                .FirstOrDefaultAsync();
        Assert.NotEqual(0, alternateOwnerId);

        var alternateOwner = await _userService.GetAsync(alternateOwnerId);
        Assert.NotNull(alternateOwner);

        Planet? secondaryPlanet = null;
        try
        {
            var createPlanetResult = await _planetService.CreateAsync(new Planet()
            {
                Name = $"cache-isolation-{Guid.NewGuid():N}".Substring(0, 24),
                Description = "Role cache isolation regression test",
                OwnerId = alternateOwner.Id,
                Public = false,
                Discoverable = false,
                Nsfw = false
            }, alternateOwner);

            Assert.True(createPlanetResult.Success, createPlanetResult.Message);
            Assert.NotNull(createPlanetResult.Data);
            secondaryPlanet = createPlanetResult.Data;

            var addMemberResult = await _planetMemberService.AddMemberAsync(secondaryPlanet.Id, _client.Me.Id);
            Assert.True(addMemberResult.Success, addMemberResult.Message);
            Assert.NotNull(addMemberResult.Data);

            var secondaryMember = addMemberResult.Data;
            var secondaryAccess = await _planetService.GetMemberChannelsAsync(secondaryMember.Id);
            Assert.NotNull(secondaryAccess);
            Assert.NotEmpty(secondaryAccess);

            var primaryAccessAfter = await _planetService.GetMemberChannelsAsync(primaryMember.Id);
            Assert.NotNull(primaryAccessAfter);
            Assert.True(primaryAccessAfter.Contains(primaryChannel.Id),
                "Accessing another planet should not poison channel access for the original planet.");
            Assert.DoesNotContain(primaryAccessAfter.List, c => c.PlanetId != primaryPlanet.Id);
        }
        finally
        {
            if (secondaryPlanet is not null)
            {
                await _planetService.DeleteAsync(secondaryPlanet.Id);
            }
        }
    }
}
