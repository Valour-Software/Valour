using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetMessageService
{
    private readonly ValourDB _db;
    private readonly CoreHubService _coreHub;

    public PlanetMessageService(
        ValourDB db,
        CoreHubService coreHub)
    {
        _db = db;
        _coreHub = coreHub;
    }
}