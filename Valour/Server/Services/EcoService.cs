namespace Valour.Server.Services;

/// <summary>
/// The EcoService handles economic transactions and trading for platform-wide
/// and planet economies
/// </summary>
public class EcoService
{
    private readonly ValourDB _db;
    private readonly ILogger<EcoService> _logger;
    private readonly CoreHubService _coreHub;

    public EcoService(ValourDB db, ILogger<EcoService> logger, CoreHubService coreHub)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
    }

    public async ValueTask<Currency> GetCurrencyAsync(long id) =>
        (await _db.Currencies.FindAsync(id)).ToModel();
}
