using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using User = Valour.Server.Models.User;
using AuthToken = Valour.Server.Models.AuthToken;

namespace Valour.Server.Services;

/// <summary>
/// Service for managing bot accounts
/// </summary>
public class BotService
{
    private const int MaxBotsPerUser = 10;

    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly TokenService _tokenService;
    private readonly ILogger<BotService> _logger;

    public BotService(
        ValourDb db,
        UserService userService,
        TokenService tokenService,
        ILogger<BotService> logger)
    {
        _db = db;
        _userService = userService;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all bots owned by the specified user
    /// </summary>
    public async Task<List<User>> GetUserBotsAsync(long ownerId)
    {
        return await _db.Users
            .Where(x => x.OwnerId == ownerId && x.Bot)
            .Select(x => x.ToModel())
            .ToListAsync();
    }

    /// <summary>
    /// Gets a specific bot by ID
    /// </summary>
    public async Task<User> GetBotAsync(long botId)
    {
        var user = await _db.Users.FindAsync(botId);
        if (user is null || !user.Bot)
            return null;
        return user.ToModel();
    }

    /// <summary>
    /// Creates a new bot account
    /// </summary>
    public async Task<TaskResult<(User Bot, string Token)>> CreateBotAsync(long ownerId, string name)
    {
        // Validate owner exists and is not a bot
        var owner = await _db.Users.FindAsync(ownerId);
        if (owner is null)
            return new(false, "Owner not found");

        if (owner.Bot)
            return new(false, "Bots cannot create other bots");

        // Check bot limit
        var botCount = await _db.Users.CountAsync(x => x.OwnerId == ownerId && x.Bot);
        if (botCount >= MaxBotsPerUser)
            return new(false, $"You can only create up to {MaxBotsPerUser} bots");

        // Validate bot name
        var nameResult = UserUtils.TestUsername(name);
        if (!nameResult.Success)
            return new(false, nameResult.Message);

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            // Create the bot user
            var bot = new Valour.Database.User
            {
                Id = IdManager.Generate(),
                Name = name,
                Tag = await _userService.GetUniqueTag(name),
                TimeJoined = DateTime.UtcNow,
                TimeLastActive = DateTime.UtcNow,
                Bot = true,
                OwnerId = ownerId,
                Compliance = true, // Bots don't need compliance
                HasAnimatedAvatar = false,
                HasCustomAvatar = false,
                Version = 0
            };

            _db.Users.Add(bot);
            await _db.SaveChangesAsync();

            // Create a bot token (long-lived, 100 years)
            var token = new Valour.Database.AuthToken
            {
                Id = "bot-" + Guid.NewGuid().ToString(),
                AppId = "BOT",
                UserId = bot.Id,
                Scope = UserPermissions.FullControl.Value,
                TimeCreated = DateTime.UtcNow,
                TimeExpires = DateTime.UtcNow.AddYears(100),
                IssuedAddress = "BOT_TOKEN"
            };

            _db.AuthTokens.Add(token);
            await _db.SaveChangesAsync();

            await tran.CommitAsync();

            return new(true, "Bot created successfully", (bot.ToModel(), token.Id));
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, "Error creating bot");
            return new(false, "An unexpected error occurred");
        }
    }

    /// <summary>
    /// Deletes a bot account
    /// </summary>
    public async Task<TaskResult> DeleteBotAsync(long botId, long ownerId)
    {
        var bot = await _db.Users.FindAsync(botId);
        if (bot is null)
            return new(false, "Bot not found");

        if (!bot.Bot)
            return new(false, "Specified user is not a bot");

        if (bot.OwnerId != ownerId)
            return new(false, "You do not own this bot");

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            // Remove all tokens for this bot
            var tokens = await _db.AuthTokens.Where(x => x.UserId == botId).ToListAsync();
            foreach (var token in tokens)
            {
                _tokenService.RemoveFromQuickCache(token.Id);
            }
            _db.AuthTokens.RemoveRange(tokens);

            // Remove the bot user
            _db.Users.Remove(bot);
            await _db.SaveChangesAsync();

            await tran.CommitAsync();

            return new(true, "Bot deleted successfully");
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, "Error deleting bot");
            return new(false, "An unexpected error occurred");
        }
    }

    /// <summary>
    /// Regenerates a bot's token
    /// </summary>
    public async Task<TaskResult<string>> RegenerateBotTokenAsync(long botId, long ownerId)
    {
        var bot = await _db.Users.FindAsync(botId);
        if (bot is null)
            return new(false, "Bot not found");

        if (!bot.Bot)
            return new(false, "Specified user is not a bot");

        if (bot.OwnerId != ownerId)
            return new(false, "You do not own this bot");

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            // Remove old tokens
            var oldTokens = await _db.AuthTokens.Where(x => x.UserId == botId).ToListAsync();
            foreach (var oldToken in oldTokens)
            {
                _tokenService.RemoveFromQuickCache(oldToken.Id);
            }
            _db.AuthTokens.RemoveRange(oldTokens);

            // Create new token
            var newToken = new Valour.Database.AuthToken
            {
                Id = "bot-" + Guid.NewGuid().ToString(),
                AppId = "BOT",
                UserId = botId,
                Scope = UserPermissions.FullControl.Value,
                TimeCreated = DateTime.UtcNow,
                TimeExpires = DateTime.UtcNow.AddYears(100),
                IssuedAddress = "BOT_TOKEN"
            };

            _db.AuthTokens.Add(newToken);
            await _db.SaveChangesAsync();

            await tran.CommitAsync();

            return new(true, "Token regenerated successfully", newToken.Id);
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e, "Error regenerating bot token");
            return new(false, "An unexpected error occurred");
        }
    }

    /// <summary>
    /// Updates a bot's information
    /// </summary>
    public async Task<TaskResult<User>> UpdateBotAsync(long botId, long ownerId, UpdateBotRequest request)
    {
        var bot = await _db.Users.FindAsync(botId);
        if (bot is null)
            return new(false, "Bot not found");

        if (!bot.Bot)
            return new(false, "Specified user is not a bot");

        if (bot.OwnerId != ownerId)
            return new(false, "You do not own this bot");

        try
        {
            if (request.Status != null)
            {
                if (request.Status.Length > 128)
                    return new(false, "Status must be 128 characters or less");

                bot.Status = request.Status;
            }

            await _db.SaveChangesAsync();

            return new(true, "Bot updated successfully", bot.ToModel());
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error updating bot");
            return new(false, "An unexpected error occurred");
        }
    }

    /// <summary>
    /// Checks if a user owns a specific bot
    /// </summary>
    public async Task<bool> OwnsBotAsync(long ownerId, long botId)
    {
        return await _db.Users.AnyAsync(x => x.Id == botId && x.OwnerId == ownerId && x.Bot);
    }
}
