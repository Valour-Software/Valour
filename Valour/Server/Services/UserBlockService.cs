using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class UserBlockService
{
    private readonly ValourDb _db;
    private readonly ILogger<UserBlockService> _logger;

    public UserBlockService(
        ValourDb db,
        ILogger<UserBlockService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TaskResult<UserBlock>> BlockUserAsync(long userId, long blockedUserId, BlockType blockType)
    {
        if (userId == blockedUserId)
            return new(false, "You cannot block yourself.");

        var targetExists = await _db.Users.AnyAsync(x => x.Id == blockedUserId);
        if (!targetExists)
            return new(false, "User not found.");

        try
        {
            var existing = await _db.UserBlocks.FirstOrDefaultAsync(
                x => x.UserId == userId && x.BlockedUserId == blockedUserId);

            if (existing is not null)
            {
                return new(false, "User is already blocked.");
            }

            var dbBlock = new Valour.Database.UserBlock
            {
                Id = IdManager.Generate(),
                UserId = userId,
                BlockedUserId = blockedUserId,
                BlockType = blockType,
                CreatedAt = DateTime.UtcNow
            };

            await _db.UserBlocks.AddAsync(dbBlock);
            await _db.SaveChangesAsync();

            var persisted = await _db.UserBlocks
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == dbBlock.Id);

            if (persisted is null)
            {
                _logger.LogError(
                    "User block insert reported success but row was not found. userId={UserId}, blockedUserId={BlockedUserId}, id={Id}",
                    userId, blockedUserId, dbBlock.Id);
                return new(false, "Failed to persist block.");
            }

            return new(true, "User blocked.", persisted.ToModel());
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Block insert failed with DbUpdateException. userId={UserId}, blockedUserId={BlockedUserId}, blockType={BlockType}",
                userId, blockedUserId, blockType);

            _db.ChangeTracker.Clear();

            var alreadyBlocked = await _db.UserBlocks
                .AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.BlockedUserId == blockedUserId);

            if (alreadyBlocked)
                return new(false, "User is already blocked.");

            return new(false, "Failed to persist block.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to block user. userId={UserId}, blockedUserId={BlockedUserId}, blockType={BlockType}",
                userId, blockedUserId, blockType);
            return new(false, "Failed to persist block.");
        }
    }

    public async Task<TaskResult> UnblockUserAsync(long userId, long blockedUserId)
    {
        try
        {
            var block = await _db.UserBlocks.FirstOrDefaultAsync(
                x => x.UserId == userId && x.BlockedUserId == blockedUserId);

            if (block is null)
                return new(false, "Block not found.");

            _db.UserBlocks.Remove(block);
            await _db.SaveChangesAsync();

            return new(true, "User unblocked.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to unblock user. userId={UserId}, blockedUserId={BlockedUserId}",
                userId, blockedUserId);
            return new(false, "Failed to remove block.");
        }
    }

    public async Task<List<UserBlock>> GetBlocksAsync(long userId)
    {
        var dbBlocks = await _db.UserBlocks
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync();

        return dbBlocks.Select(x => x.ToModel()).ToList();
    }

    /// <summary>
    /// Returns true if userId has blocked targetUserId (any block type).
    /// </summary>
    public async Task<bool> IsBlockedAsync(long userId, long targetUserId)
    {
        return await _db.UserBlocks.AnyAsync(
            x => x.UserId == userId && x.BlockedUserId == targetUserId);
    }

    /// <summary>
    /// Returns true if either user has blocked the other (any block type).
    /// Used for DM and friend request enforcement.
    /// </summary>
    public async Task<bool> IsBlockedEitherWayAsync(long userA, long userB)
    {
        return await _db.UserBlocks.AnyAsync(
            x => (x.UserId == userA && x.BlockedUserId == userB) ||
                 (x.UserId == userB && x.BlockedUserId == userA));
    }

    /// <summary>
    /// Returns true if a two-way block exists between the users (in either direction).
    /// Used for profile visibility enforcement.
    /// </summary>
    public async Task<bool> IsTwoWayBlockedAsync(long userA, long userB)
    {
        return await _db.UserBlocks.AnyAsync(
            x => x.BlockType == BlockType.TwoWay &&
                 ((x.UserId == userA && x.BlockedUserId == userB) ||
                  (x.UserId == userB && x.BlockedUserId == userA)));
    }

    /// <summary>
    /// Returns the set of user IDs whose messages should be hidden from the given user.
    /// Includes: all users they've blocked (one-way or two-way) + users who have two-way blocked them.
    /// </summary>
    public async Task<HashSet<long>> GetEffectiveHiddenUserIdsAsync(long userId)
    {
        // Users this user has blocked (any type)
        var blockedByMe = await _db.UserBlocks
            .Where(x => x.UserId == userId)
            .Select(x => x.BlockedUserId)
            .ToListAsync();

        // Users who have two-way blocked this user
        var twoWayBlockedMe = await _db.UserBlocks
            .Where(x => x.BlockedUserId == userId && x.BlockType == BlockType.TwoWay)
            .Select(x => x.UserId)
            .ToListAsync();

        var hidden = new HashSet<long>(blockedByMe);
        foreach (var id in twoWayBlockedMe)
            hidden.Add(id);

        return hidden;
    }
}
