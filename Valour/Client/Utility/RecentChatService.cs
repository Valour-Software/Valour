using Valour.Client.Storage;
using Valour.Sdk.Client;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Utility;

public sealed class RecentChatService
{
    private const string StorageKeyPrefix = "VALOUR_RECENT_CHATS_V1";
    private const int MaxRecentChats = 24;

    private readonly IAppStorage _storage;
    private readonly ValourClient _client;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string _loadedStorageKey;
    private List<RecentChatEntry> _entries;

    public event Action Changed;

    public RecentChatService(IAppStorage storage, ValourClient client)
    {
        _storage = storage;
        _client = client;
    }

    public async Task<IReadOnlyList<RecentChatEntry>> GetRecentChatsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureLoadedAsync();

            return _entries
                .OrderByDescending(x => x.OpenedAtUtc)
                .Select(x => x.Clone())
                .ToList();
        }
        catch
        {
            return Array.Empty<RecentChatEntry>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordOpenedAsync(Channel channel)
    {
        if (channel is null || !CanTrack(channel))
            return;

        try
        {
            var entry = new RecentChatEntry
            {
                Kind = GetKind(channel),
                ChannelId = channel.Id,
                PlanetId = channel.PlanetId,
                OpenedAtUtc = DateTime.UtcNow
            };

            await _lock.WaitAsync();
            try
            {
                await EnsureLoadedAsync();

                _entries.RemoveAll(x => IsSameChat(x, entry));
                _entries.Insert(0, entry);

                if (_entries.Count > MaxRecentChats)
                    _entries.RemoveRange(MaxRecentChats, _entries.Count - MaxRecentChats);

                await _storage.SetAsync(GetStorageKey(), _entries);
            }
            finally
            {
                _lock.Release();
            }

            Changed?.Invoke();
        }
        catch
        {
            // Recent chat history should never block opening a chat.
        }
    }

    private async Task EnsureLoadedAsync()
    {
        var storageKey = GetStorageKey();
        if (_entries is not null && _loadedStorageKey == storageKey)
            return;

        _loadedStorageKey = storageKey;
        _entries = await _storage.GetAsync<List<RecentChatEntry>>(storageKey) ?? new List<RecentChatEntry>();
        _entries = _entries
            .Where(IsValidEntry)
            .OrderByDescending(x => x.OpenedAtUtc)
            .GroupBy(GetChatKey)
            .Select(x => x.First())
            .Take(MaxRecentChats)
            .ToList();
    }

    private string GetStorageKey() => $"{StorageKeyPrefix}:{_client.Me?.Id ?? 0}";

    private static bool CanTrack(Channel channel) =>
        channel.ChannelType is ChannelTypeEnum.PlanetChat or ChannelTypeEnum.DirectChat or ChannelTypeEnum.GroupChat;

    private static RecentChatKind GetKind(Channel channel) =>
        channel.PlanetId is not null
            ? RecentChatKind.Planet
            : channel.ChannelType == ChannelTypeEnum.GroupChat
                ? RecentChatKind.Group
                : RecentChatKind.Direct;

    private static bool IsValidEntry(RecentChatEntry entry) =>
        entry is not null &&
        entry.ChannelId > 0 &&
        entry.OpenedAtUtc != default &&
        (entry.Kind != RecentChatKind.Planet || entry.PlanetId is not null);

    private static bool IsSameChat(RecentChatEntry left, RecentChatEntry right) =>
        GetChatKey(left) == GetChatKey(right);

    private static string GetChatKey(RecentChatEntry entry) =>
        entry.Kind == RecentChatKind.Planet
            ? $"{entry.Kind}:{entry.PlanetId}"
            : $"{entry.Kind}:{entry.ChannelId}";
}

public enum RecentChatKind
{
    Planet,
    Direct,
    Group
}

public sealed class RecentChatEntry
{
    public RecentChatKind Kind { get; set; }
    public long ChannelId { get; set; }
    public long? PlanetId { get; set; }
    public DateTime OpenedAtUtc { get; set; }

    public RecentChatEntry Clone() => new()
    {
        Kind = Kind,
        ChannelId = ChannelId,
        PlanetId = PlanetId,
        OpenedAtUtc = OpenedAtUtc
    };
}
