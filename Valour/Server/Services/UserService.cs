using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Users;

namespace Valour.Server.Services;

public class UserService
{
    private readonly ValourDB _db;
    private readonly TokenService _tokenService;

    public UserService(ValourDB db, TokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    /// <summary>
    /// Returns the user for the given id
    /// </summary>
    public async Task<User> GetAsync(long id) =>
        await _db.Users.FindAsync(id);


    /// <summary>
    /// Returns the auth token for the current context
    /// </summary>

    public async ValueTask<AuthToken> GetCurrentToken() =>
        await _tokenService.GetCurrentToken();
}