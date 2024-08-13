namespace Valour.Server.Services;

public class BlockedUserService
{
    private readonly ValourDB _db;
    private readonly ILogger<BlockedUserService> _logger;
    
    public BlockedUserService(ValourDB db, ILogger<BlockedUserService> logger)
    {
        _db = db;
        _logger = logger;
    }
}