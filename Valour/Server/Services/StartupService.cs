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
        // Check for Victor
        var victorExists = await _db.Users.AnyAsync(x => x.Id == ISharedUser.VictorUserId);
        if (!victorExists)
        {
            _logger.LogInformation("Creating Victor User");
            
            var result = await _registerService.RegisterUserAsync(new RegisterUserRequest()
            {
                Email = "victor@valour.gg",
                Locality = Locality.General,
                Password = Guid.NewGuid().ToString() + "!",
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
            }, victor);
            
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
}