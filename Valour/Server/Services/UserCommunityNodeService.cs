using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Valour.Database;
using Valour.Shared;
using Valour.Shared.Nodes;

namespace Valour.Server.Services;

public class UserCommunityNodeService
{
    private readonly ValourDb _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UserCommunityNodeService> _logger;

    public UserCommunityNodeService(
        ValourDb db,
        IHttpClientFactory httpClientFactory,
        ILogger<UserCommunityNodeService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<List<SavedCommunityNode>> GetForUserAsync(long userId)
    {
        return await _db.UserCommunityNodes
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.CanonicalOrigin)
            .Select(x => ToSharedModel(x))
            .ToListAsync();
    }

    public async Task<TaskResult<SavedCommunityNode>> AddAsync(long userId, string origin)
    {
        var manifestResult = await FetchManifestAsync(origin);
        if (!manifestResult.Success || manifestResult.Data is null)
            return TaskResult<SavedCommunityNode>.FromFailure(manifestResult);

        var manifest = manifestResult.Data;
        if (manifest.Mode != NodeMode.Community)
            return TaskResult<SavedCommunityNode>.FromFailure("Only community nodes can be saved here.");

        var canonicalOrigin = NormalizeInputOrigin(manifest.CanonicalOrigin);
        var authorityOrigin = NormalizeInputOrigin(string.IsNullOrWhiteSpace(manifest.AuthorityOrigin)
            ? canonicalOrigin
            : manifest.AuthorityOrigin);

        var record = await _db.UserCommunityNodes
            .FirstOrDefaultAsync(x => x.UserId == userId && x.CanonicalOrigin == canonicalOrigin);

        if (record is null)
        {
            record = new UserCommunityNode
            {
                UserId = userId,
                TimeAdded = DateTime.UtcNow
            };

            await _db.UserCommunityNodes.AddAsync(record);
        }

        record.NodeId = string.IsNullOrWhiteSpace(manifest.NodeId)
            ? canonicalOrigin
            : manifest.NodeId.Trim();
        record.Name = string.IsNullOrWhiteSpace(manifest.Name)
            ? record.NodeId
            : manifest.Name.Trim();
        record.CanonicalOrigin = canonicalOrigin;
        record.AuthorityOrigin = authorityOrigin;
        record.Mode = manifest.Mode;

        await _db.SaveChangesAsync();

        return TaskResult<SavedCommunityNode>.FromData(ToSharedModel(record));
    }

    public async Task<TaskResult> RemoveAsync(long userId, long savedNodeId)
    {
        var record = await _db.UserCommunityNodes
            .FirstOrDefaultAsync(x => x.Id == savedNodeId && x.UserId == userId);

        if (record is null)
            return TaskResult.FromFailure("Saved community node not found.");

        _db.UserCommunityNodes.Remove(record);
        await _db.SaveChangesAsync();

        return TaskResult.FromSuccess("Community node removed.");
    }

    private async Task<TaskResult<NodeManifest>> FetchManifestAsync(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return TaskResult<NodeManifest>.FromFailure("Enter a node URL first.");

        var normalizedOrigin = NormalizeInputOrigin(origin);
        if (string.IsNullOrWhiteSpace(normalizedOrigin))
            return TaskResult<NodeManifest>.FromFailure("Enter a valid node URL.");

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(normalizedOrigin.TrimEnd('/') + "/");

            var response = await client.GetAsync("api/node/manifest");
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();
                return TaskResult<NodeManifest>.FromFailure(
                    string.IsNullOrWhiteSpace(message)
                        ? $"Node handshake failed with status {(int)response.StatusCode}."
                        : message,
                    (int)response.StatusCode);
            }

            var manifest = await response.Content.ReadFromJsonAsync<NodeManifest>();
            if (manifest is null)
                return TaskResult<NodeManifest>.FromFailure("Node did not return a valid manifest.");

            manifest.NodeId = string.IsNullOrWhiteSpace(manifest.NodeId)
                ? NormalizeInputOrigin(manifest.CanonicalOrigin)
                : manifest.NodeId.Trim();
            manifest.Name = string.IsNullOrWhiteSpace(manifest.Name)
                ? manifest.NodeId
                : manifest.Name.Trim();
            manifest.CanonicalOrigin = NormalizeInputOrigin(string.IsNullOrWhiteSpace(manifest.CanonicalOrigin)
                ? normalizedOrigin
                : manifest.CanonicalOrigin);
            manifest.AuthorityOrigin = NormalizeInputOrigin(string.IsNullOrWhiteSpace(manifest.AuthorityOrigin)
                ? manifest.CanonicalOrigin
                : manifest.AuthorityOrigin);

            return TaskResult<NodeManifest>.FromData(manifest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch community node manifest from {Origin}", origin);
            return TaskResult<NodeManifest>.FromFailure("Could not reach that node.");
        }
    }

    private static SavedCommunityNode ToSharedModel(UserCommunityNode record)
    {
        return new SavedCommunityNode
        {
            Id = record.Id,
            NodeId = record.NodeId,
            Name = record.Name,
            CanonicalOrigin = record.CanonicalOrigin,
            AuthorityOrigin = record.AuthorityOrigin,
            Mode = record.Mode,
            TimeAdded = record.TimeAdded
        };
    }

    private static string NormalizeInputOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return null;

        var value = origin.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;

        try
        {
            return CommunityNodeTokenService.NormalizeOrigin(value);
        }
        catch
        {
            return null;
        }
    }
}
