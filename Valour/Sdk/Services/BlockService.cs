using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

public class BlockService : ServiceBase
{
    public HybridEvent BlocksChanged;

    private readonly ValourClient _client;
    private readonly object _lock = new();

    public readonly List<UserBlock> Blocks = new();
    private readonly HashSet<long> _blockedUserIds = new();

    private static readonly LogOptions LogOptions = new(
        "BlockService",
        "#036bfc",
        "#fc0356",
        "#fc8403"
    );

    public BlockService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }

    /// <summary>
    /// Fetches all blocks from the server
    /// </summary>
    public async Task FetchBlocksAsync()
    {
        var result = await _client.AccountNode.GetJsonAsync<List<UserBlock>>("api/users/me/blocks");
        if (!result.Success || result.Data is null)
        {
            LogError("Error loading blocks.");
            return;
        }

        lock (_lock)
        {
            Blocks.Clear();
            _blockedUserIds.Clear();
            foreach (var block in result.Data)
            {
                Blocks.Add(block);
                _blockedUserIds.Add(block.BlockedUserId);
            }
        }

        Log($"Loaded {Blocks.Count} blocks.");
        BlocksChanged?.Invoke();
    }

    /// <summary>
    /// Returns true if the given user is blocked by the current user
    /// </summary>
    public bool IsBlocked(long userId)
    {
        lock (_lock)
        {
            return _blockedUserIds.Contains(userId);
        }
    }

    /// <summary>
    /// Blocks a user and updates the local cache
    /// </summary>
    public async Task<TaskResult<UserBlock>> BlockUserAsync(long targetUserId, BlockType blockType)
    {
        // Follow client CRUD/cache patterns: prevent duplicate block submits from local state.
        if (IsBlocked(targetUserId))
            return TaskResult<UserBlock>.FromFailure("User is already blocked.");

        var result = await _client.AccountNode.PostAsyncWithResponse<UserBlock>($"api/userblocks/{targetUserId}/{(int)blockType}");
        if (result.Success && result.Data is not null)
        {
            lock (_lock)
            {
                Blocks.RemoveAll(x => x.BlockedUserId == targetUserId);
                Blocks.Add(result.Data);
                _blockedUserIds.Add(targetUserId);
            }

            BlocksChanged?.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Unblocks a user and updates the local cache
    /// </summary>
    public async Task<TaskResult> UnblockUserAsync(long targetUserId)
    {
        var result = await _client.AccountNode.DeleteAsync($"api/userblocks/{targetUserId}");
        if (result.Success)
        {
            lock (_lock)
            {
                Blocks.RemoveAll(x => x.BlockedUserId == targetUserId);
                _blockedUserIds.Remove(targetUserId);
            }

            BlocksChanged?.Invoke();
        }

        return result;
    }
}
