using Valour.Config.Configs;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class StartupService
{
    private readonly ValourDb _db;
    private readonly PlanetService _planetService;
    private readonly UserService _userService;
    private readonly RegisterService _registerService;
    private readonly ILogger<StartupService> _logger;
    
    public StartupService(
        PlanetService planetService,
        UserService userService, ValourDb db, RegisterService registerService, ILogger<StartupService> logger)
    {
        _planetService = planetService;
        _userService = userService;
        _db = db;
        _registerService = registerService;
        _logger = logger;
    }
    
    /// <summary>
    /// Ensures that Valour Central and Victor are both ready
    /// </summary>
    public async Task EnsureVictorAndValourCentralReady()
    {
        // Community nodes are not independent hubs. Creating an unregistered
        // local Valour Central here would collide with the hub-reserved global
        // id space and expose a phantom official community.
        if (FederationNodeService.NodeEnabled)
        {
            _logger.LogInformation("Skipping Valour Central bootstrap on community node");
            return;
        }

        // Check for Victor
        var victorExists = await _db.Users.AnyAsync(x => x.Id == ISharedUser.VictorUserId);
        if (!victorExists)
        {
            _logger.LogInformation("Creating Victor User");
            
            var result = await _registerService.RegisterUserAsync(new RegisterUserRequest()
            {
                Email = "victor@valour.gg",
                Password = "T" + Guid.NewGuid().ToString().Substring(0, 10) + "!",
                Username = "Victor",
                DateOfBirth = new DateTime(1990, 1, 1),
            }, null, skipEmail: true, forceId: ISharedUser.VictorUserId);
            
            if (!result.Success)
            {
                _logger.LogError("Failed to create Victor User");
                _logger.LogError("Error: {Error}", result.Message);
                return;
            }
        }
        else
        {
            _logger.LogInformation("Victor already exists");
        }

        var victor = await _userService.GetAsync(ISharedUser.VictorUserId);

        // Check for Valour Central
        var valourCentralExists = await _db.Planets.AnyAsync(x => x.Id == ISharedPlanet.ValourCentralId);
        if (!valourCentralExists)
        {
            _logger.LogInformation("Creating Valour Central");
            
            var result = await _planetService.CreateAsync(new Planet()
            {
                Name = "Valour Central",
                Description = "The central hub of Valour",
                OwnerId = ISharedUser.VictorUserId,
                Discoverable = true,
                Public = true,
            }, victor, ISharedPlanet.ValourCentralId);
            
            if (!result.Success)
            {
                _logger.LogError("Failed to create Valour Central");
                _logger.LogError("Error: {Error}", result.Message);
                return;
            }
        }
        else
        {
            _logger.LogInformation("Valour Central already exists");
        }
    }

    /// <summary>
    /// Creates a verified staff account on first run when bootstrap admin
    /// credentials are configured and no staff account exists yet.
    /// Intended for self-hosted instances (see Bootstrap config section).
    /// </summary>
    public async Task EnsureBootstrapAdminAsync()
    {
        var config = BootstrapConfig.Current;
        if (string.IsNullOrWhiteSpace(config?.AdminEmail) ||
            string.IsNullOrWhiteSpace(config?.AdminPassword))
            return;

        if (await _db.Users.AnyAsync(x => x.ValourStaff))
        {
            _logger.LogInformation("Staff account already exists; skipping bootstrap admin");
            return;
        }

        _logger.LogInformation("Creating bootstrap admin account {Username}", config.AdminUsername);

        var result = await _registerService.RegisterUserAsync(new RegisterUserRequest()
        {
            Email = config.AdminEmail,
            Password = config.AdminPassword,
            Username = config.AdminUsername,
            DateOfBirth = new DateTime(1990, 1, 1),
            Source = "bootstrap",
        }, null, skipEmail: true);

        if (!result.Success || result.Data is null)
        {
            _logger.LogError("Failed to create bootstrap admin: {Error}", result.Message);
            return;
        }

        var user = await _db.Users.FindAsync(result.Data.Id);
        if (user is not null)
        {
            user.ValourStaff = true;
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("Bootstrap admin created and granted staff");
    }
}
