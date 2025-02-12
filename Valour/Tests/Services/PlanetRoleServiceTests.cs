using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services
{
    [Collection("ApiCollection")]
    public class PlanetRoleServiceTests : IAsyncLifetime
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ValourClient _client;
        private readonly IServiceScope _scope;
        private readonly HostedPlanetService _hostedService;
        private readonly PlanetRoleService _roleService;
        private readonly PlanetService _planetService;
        private readonly UserService _userService;
        private readonly ValourDb _db;

        // This is the ValourCentral planet ID. The "Test User" (_client.Me) is a member here.
        private readonly long _valourCentralId = ISharedPlanet.ValourCentralId;

        // Keep track of created roles so they can be cleaned up after tests
        private readonly List<PlanetRole> _createdRoles = new();

        public PlanetRoleServiceTests(LoginTestFixture fixture)
        {
            _client = fixture.Client;
            _factory = fixture.Factory;

            // Create a new scope for each test class instance
            _scope = _factory.Services.CreateScope();
            _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
            _roleService = _scope.ServiceProvider.GetRequiredService<PlanetRoleService>();
            _planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
            _userService = _scope.ServiceProvider.GetRequiredService<UserService>();
            _hostedService = _scope.ServiceProvider.GetRequiredService<HostedPlanetService>();
        }

        // Clean up any created roles after all tests in this class have run
        public async Task DisposeAsync()
        {
            foreach (var role in _createdRoles)
            {
                // Skip if role was already deleted or if it's somehow null
                if (role is null) 
                    continue;
                
                // Attempt to delete
                await _roleService.DeleteAsync(role.PlanetId, role.Id);
            }
        }

        // xUnit requires these to fulfill IAsyncLifetime
        public Task InitializeAsync() => Task.CompletedTask;

        [Fact]
        public async Task CreateRole()
        {
            // Create a valid role
            var roleToCreate = new PlanetRole
            {
                Name = "Test Role",
                Color = "#abcdef",
                PlanetId = _valourCentralId,
                IsAdmin = false
            };

            var result = await _roleService.CreateAsync(roleToCreate);
            Assert.True(result.Success, $"Failed to create role: {result.Message}");
            Assert.NotNull(result.Data);

            var createdRole = result.Data;
            Assert.NotEqual(0, createdRole.Id);
            Assert.Equal("#abcdef", createdRole.Color);

            // Track it for cleanup
            _createdRoles.Add(createdRole);
        }

        [Fact]
        public async Task CreateRole_InvalidColor()
        {
            // Create a role with an invalid color string
            var roleToCreate = new PlanetRole
            {
                Name = "Invalid Color Role",
                Color = "blue", // Not a valid hex
                PlanetId = _valourCentralId
            };

            var result = await _roleService.CreateAsync(roleToCreate);

            if (result.Success)
            {
                // Clean up the role if it was created
                _createdRoles.Add(result.Data);
            }
            
            Assert.False(result.Success);
            Assert.Contains("Invalid hex color", result.Message);
        }

        [Fact]
        public async Task CreateRole_NoColorUsesDefault()
        {
            // Create a role without specifying color
            var roleToCreate = new PlanetRole
            {
                Name = "No Color",
                PlanetId = _valourCentralId
            };

            var result = await _roleService.CreateAsync(roleToCreate);
            Assert.True(result.Success, $"Role creation failed: {result.Message}");
            Assert.NotNull(result.Data);

            var createdRole = result.Data;
            Assert.Equal("#ffffff", createdRole.Color);

            _createdRoles.Add(createdRole);
        }

        [Fact]
        public async Task UpdateRole_Success()
        {
            // First, create a new role
            var roleToCreate = new PlanetRole
            {
                Name = "Update Test Role",
                Color = "#123456",
                PlanetId = _valourCentralId
            };
            
            var createResult = await _roleService.CreateAsync(roleToCreate);
            Assert.True(createResult.Success, $"Create failed: {createResult.Message}");
            
            var createdRole = createResult.Data;
            _createdRoles.Add(createdRole);

            // Modify a few properties
            createdRole.Name = "Updated Name";
            createdRole.Color = "#654321";

            var updateResult = await _roleService.UpdateAsync(createdRole);
            Assert.True(updateResult.Success, $"Update failed: {updateResult.Message}");

            var updatedRole = updateResult.Data;
            Assert.Equal("Updated Name", updatedRole.Name);
            Assert.Equal("#654321", updatedRole.Color);
        }

        [Fact]
        public async Task UpdateRole_AttemptChangePlanetIdFails()
        {
            // Create a role
            var roleToCreate = new PlanetRole
            {
                Name = "Change PlanetId Fails",
                Color = "#bbccdd",
                PlanetId = _valourCentralId
            };
            
            var createResult = await _roleService.CreateAsync(roleToCreate);
            Assert.True(createResult.Success);
            var createdRole = createResult.Data;
            _createdRoles.Add(createdRole);
            
            var creatorUser = await _userService.GetAsync(_client.Me.Id);

            if (!await _planetService.ExistsAsync(9001))
            {
                // Create another planet
                await _planetService.CreateAsync(new Planet()
                {
                    Name = "Another Planet",
                    Description = "Another planet for testing",
                    OwnerId = _client.Me.Id,
                }, creatorUser, 9001);
            }

            // Attempt to move the role to a different planet
            createdRole.PlanetId = 9001; // different planet ID

            var updateResult = await _roleService.UpdateAsync(createdRole);
            
            Assert.False(updateResult.Success);
            Assert.Contains("change the planet", updateResult.Message);
            
            // Revert the change
            createdRole.PlanetId = _valourCentralId;
        }

        [Fact]
        public async Task UpdateRole_AttemptChangePositionFails()
        {
            // Create a role
            var roleToCreate = new PlanetRole
            {
                Name = "Change Position Fails",
                Color = "#001122",
                PlanetId = _valourCentralId
            };
            var createResult = await _roleService.CreateAsync(roleToCreate);
            Assert.True(createResult.Success);
            var createdRole = createResult.Data;
            _createdRoles.Add(createdRole);

            // Attempt to change position
            createdRole.Position = 999;

            var updateResult = await _roleService.UpdateAsync(createdRole);
            Assert.False(updateResult.Success);
            Assert.Contains("Position cannot be changed", updateResult.Message);
        }

        [Fact]
        public async Task UpdateRole_AttemptChangeDefaultFails()
        {
            // Create a role
            var roleToCreate = new PlanetRole
            {
                Name = "Change Default Fails",
                PlanetId = _valourCentralId
            };
            var createResult = await _roleService.CreateAsync(roleToCreate);
            Assert.True(createResult.Success);
            var createdRole = createResult.Data;
            _createdRoles.Add(createdRole);

            // Attempt to change default status
            createdRole.IsDefault = true;

            var updateResult = await _roleService.UpdateAsync(createdRole);
            Assert.False(updateResult.Success);
            Assert.Contains("Cannot change default status of role", updateResult.Message);
        }

        [Fact]
        public async Task DeleteRole_Success()
        {
            // Create a role to delete
            var roleToCreate = new PlanetRole
            {
                Name = "Delete Test",
                PlanetId = _valourCentralId,
                Color = "#abcdef"
            };

            var createResult = await _roleService.CreateAsync(roleToCreate);
            Assert.True(createResult.Success);
            var createdRole = createResult.Data;

            // Deleting right away, so no need to add to cleanup
            var deleteResult = await _roleService.DeleteAsync(createdRole);
            Assert.True(deleteResult.Success);
            
            // Verify it's gone from the database
            var dbRole = await _db.PlanetRoles.FindAsync(createdRole.Id);
            Assert.Null(dbRole);
        }

        [Fact]
        public async Task DeleteRole_DefaultFails()
        {
            // Attempt to delete a default role. 

            // Get a default role from ValourCentral. 
            
            var defaultRole = await _db.PlanetRoles
                .Where(r => r.PlanetId == _valourCentralId && r.IsDefault)
                .FirstOrDefaultAsync();

            Assert.NotNull(defaultRole);

            var deleteResult = await _roleService.DeleteAsync(defaultRole.PlanetId, defaultRole.Id);
            Assert.False(deleteResult.Success);
            Assert.Contains("Cannot delete default roles", deleteResult.Message);
        }

        [Fact]
        public async Task GetAsync_ReturnsRole()
        {
            // Create a role
            var roleToCreate = new PlanetRole
            {
                Name = "Get Role Test",
                PlanetId = _valourCentralId,
                Color = "#123123"
            };

            var createResult = await _roleService.CreateAsync(roleToCreate);
            Assert.True(createResult.Success);
            var createdRole = createResult.Data;
            _createdRoles.Add(createdRole);

            // Fetch the role
            var fetchedRole = await _roleService.GetAsync(_valourCentralId, createdRole.Id);
            Assert.NotNull(fetchedRole);
            Assert.Equal(createdRole.Id, fetchedRole.Id);
            Assert.Equal("Get Role Test", fetchedRole.Name);
        }

        [Fact]
        public async Task GetAsync_NonExistentRoleReturnsNull()
        {
            // Arbitrary role ID that does not exist
            var roleId = 9999999999999;
            var fetched = await _roleService.GetAsync(_valourCentralId, roleId);
            Assert.Null(fetched);
        }

        [Fact]
        public async Task GetNodesAsync_EmptyListForNewRole()
        {
            // Create a role
            var roleToCreate = new PlanetRole
            {
                Name = "Nodes Test Role",
                PlanetId = _valourCentralId
            };

            var createResult = await _roleService.CreateAsync(roleToCreate);
            Assert.True(createResult.Success);
            var createdRole = createResult.Data;
            _createdRoles.Add(createdRole);

            // Get permission nodes
            var nodes = await _roleService.GetNodesAsync(createdRole.Id);
            Assert.Empty(nodes);
        }

        [Fact]
        public async Task EnsureHostedCache_RoleCombosUnique()
        {
            // Get hosted planet for valour central
            var hostedPlanet = await _hostedService.GetRequiredAsync(_valourCentralId);
            
            // Check role combos
            var combos = hostedPlanet.RoleMembershipCombos;
            
            // Ensure all keys are unique
            Assert.Equal(combos.Count, combos.Select(c => c.Key).Distinct().Count());
            
            // Ensure all role lists are unique
            // Kind of ugly, but...
            var roleComboValues = combos.Select(x => 
            {
                var roleIds = x.Value;
                return string.Join('-', roleIds);
            }).ToList();
            
            // Asser not null
            Assert.NotNull(roleComboValues);
            
            Assert.Equal(roleComboValues.Count(), roleComboValues.Distinct().Count());
        }
    }
}
