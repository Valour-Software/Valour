#nullable enable

using System.Collections.Concurrent;
using System.Threading;
using Valour.Server.Utilities;
using Valour.Shared.Extensions;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Server.Models;

/// <summary>
/// The HostedPlanet class is used for caching planet information on the server
/// for planets which are directly hosted by that node.
/// </summary>
public class HostedPlanet : ServerModel<long>
{
    private readonly SortedServerModelList<Channel, long> _channels = new();
    private readonly SortedServerModelList<PlanetRole, long> _roles = new();
    private readonly ServerModelList<PlanetEmoji, long> _emojis = new();
    private readonly SortedServerModelList<PlanetRule, long> _rules = new();

    // Fixed-size array for local-to-global role mapping.
    private readonly long[] _localToGlobalRoleId = new long[256];
    private volatile long[]? _localToGlobalRoleIdSnapshot;
    private volatile bool _isLocalToGlobalRoleIdDirty = true;

    // Lock for controlling access to the local-to-global array and snapshot.
    private readonly ReaderWriterLockSlim _localToGlobalRoleLock =
        new(LockRecursionPolicy.SupportsRecursion);

    public readonly PlanetPermissionsCache PermissionCache = new();

    // Channel permission inheritance maps (per-planet to avoid memory leaks)
    private readonly ConcurrentDictionary<long, long?> _inheritanceMap = new();
    private readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _inheritanceLists = new();

    private Channel _defaultChannel;
    private PlanetRole _defaultRole;
    
    // Planet lock (using your simple lock implementation)
    private readonly Lock _lock = new();
    
    public Planet Planet { get; }
    
    public HostedPlanet(
        Planet planet,
        List<Channel> channels,
        List<PlanetRole> roles,
        List<PlanetEmoji> emojis,
        List<PlanetRule> rules)
    {
        Planet = planet;
        Id = planet.Id;
        SetChannels(channels);
        SetRoles(roles);
        SetEmojis(emojis);
        SetRules(rules);
    }
    
    public void Update(Planet updated)
    {
        lock (_lock)
        {
            updated.CopyAllTo(Planet);
        }
    }
    
    // Channels //
    
    public ModelListSnapshot<Channel, long> Channels => _channels.Snapshot;
    
    public Channel? GetChannel(long id) => _channels.Get(id);
    
    public Channel GetDefaultChannel() => _defaultChannel;
    
    public void SetChannels(List<Channel> channels)
    {
        _channels.Set(channels);
        // Set default channel
        foreach (var channel in channels)
        {
            if (channel.IsDefault)
            {
                _defaultChannel = channel;
                break;
            }
        }
    }
    
    public void UpsertChannel(Channel updated)
    {
        var existing = _channels.Get(updated.Id);
        var oldPermValue = existing?.InheritsPerms ?? false;
        var newPermValue = updated.InheritsPerms;
        
        if (oldPermValue != newPermValue)
        {
            PermissionCache.ClearCacheForChannel(updated.Id);
        }
        
        var result = _channels.Upsert(updated);
        if (result.IsDefault)
        {
            _defaultChannel = result;
        }
    }
    
    public void RemoveChannel(long id)
    {
        _channels.Remove(id);
    }
    
    public void SortChannels()
    {
        _channels.Sort();
    }
    
    // Roles //

    public PlanetRole? GetRoleById(long id) => _roles.Get(id);
    
    /// <summary>
    /// Returns the global role ID for a given local role id using a snapshot for fast access.
    /// If the snapshot is outdated (dirty), it is rebuilt under a write lock.
    /// </summary>
    public long GetRoleIdByIndex(int localId)
    {
        if (_isLocalToGlobalRoleIdDirty)
        {
            _localToGlobalRoleLock.EnterWriteLock();
            try
            {
                if (_isLocalToGlobalRoleIdDirty)
                {
                    // Create a fresh snapshot of the fixed-size array.
                    _localToGlobalRoleIdSnapshot = (long[])_localToGlobalRoleId.Clone();
                    _isLocalToGlobalRoleIdDirty = false;
                }
            }
            finally
            {
                _localToGlobalRoleLock.ExitWriteLock();
            }
        }
        
        _localToGlobalRoleLock.EnterReadLock();
        try
        {
            if (_localToGlobalRoleIdSnapshot is null ||
                localId < 0 || localId >= _localToGlobalRoleIdSnapshot.Length)
            {
                return 0;
            }
            return _localToGlobalRoleIdSnapshot[localId];
        }
        finally
        {
            _localToGlobalRoleLock.ExitReadLock();
        }
    }
    
    public PlanetRole? GetRoleByIndex(int index)
    {
        long globalId = GetRoleIdByIndex(index);
        return globalId == 0 ? null : _roles.Get(globalId);
    }
    
    public PlanetRole GetDefaultRole() => _defaultRole;
    
    public void SetRoles(List<PlanetRole> roles)
    {
        _roles.Set(roles);
        // Update default role and mapping under the write lock.
        _localToGlobalRoleLock.EnterWriteLock();
        try
        {
            foreach (var role in roles)
            {
                if (role.IsDefault)
                {
                    _defaultRole = role;
                }
                // Ensure local role ID is within the fixed array range.
                if (role.FlagBitIndex >= 0 && role.FlagBitIndex < _localToGlobalRoleId.Length)
                {
                    _localToGlobalRoleId[role.FlagBitIndex] = role.Id;
                }
            }
            _isLocalToGlobalRoleIdDirty = true; // Mark snapshot as stale.
        }
        finally
        {
            _localToGlobalRoleLock.ExitWriteLock();
        }
    }
    
    public void UpsertRole(PlanetRole role)
    {
        var result = _roles.Upsert(role);
        if (result.IsDefault)
        {
            _defaultRole = result;
        }
        
        _localToGlobalRoleLock.EnterWriteLock();
        try
        {
            if (role.FlagBitIndex >= 0 && role.FlagBitIndex < _localToGlobalRoleId.Length)
            {
                _localToGlobalRoleId[role.FlagBitIndex] = role.Id;
                _isLocalToGlobalRoleIdDirty = true;
            }
        }
        finally
        {
            _localToGlobalRoleLock.ExitWriteLock();
        }
    }
    
    public void RemoveRole(long id)
    {
        _localToGlobalRoleLock.EnterWriteLock();
        try
        {
            var role = _roles.Get(id);
            if (role != null && role.FlagBitIndex >= 0 && role.FlagBitIndex < _localToGlobalRoleId.Length)
            {
                // Reset the mapping for this local role id.
                _localToGlobalRoleId[role.FlagBitIndex] = 0;
            }
            _isLocalToGlobalRoleIdDirty = true;
        }
        finally
        {
            _localToGlobalRoleLock.ExitWriteLock();
        }
        _roles.Remove(id);
    }

    public ModelListSnapshot<PlanetRole, long> Roles => _roles.Snapshot;

    // Emojis //

    public ModelListSnapshot<PlanetEmoji, long> Emojis => _emojis.Snapshot;

    public PlanetEmoji? GetEmoji(long id) => _emojis.Get(id);

    public void SetEmojis(List<PlanetEmoji> emojis)
    {
        _emojis.Set(emojis);
    }

    public void UpsertEmoji(PlanetEmoji emoji)
    {
        _emojis.Upsert(emoji);
    }

    public void RemoveEmoji(long id)
    {
        _emojis.Remove(id);
    }

    // Rules //

    public ModelListSnapshot<PlanetRule, long> Rules => _rules.Snapshot;

    public PlanetRule? GetRule(long id) => _rules.Get(id);

    public void SetRules(List<PlanetRule> rules)
    {
        _rules.Set(rules);
    }

    public void UpsertRule(PlanetRule rule)
    {
        _rules.Upsert(rule);
    }

    public void RemoveRule(long id)
    {
        _rules.Remove(id);
    }

    // Members //

    // The full set of (non-deleted) members for this planet, keyed by member id, with a
    // secondary user-id -> member-id index. Because every planet-scoped mutation is routed to
    // the hosting node, this cache is authoritative for membership and role state while loaded.
    //
    // INVARIANT: cached members are "cores" whose User is intentionally null - user data lives in
    // the node-global UserCacheService and is composed on read. These instances must never be
    // handed to external callers; expose only copies (see PlanetMemberMapper.CopyWithUser).
    private readonly ConcurrentDictionary<long, PlanetMember> _members = new();
    private readonly ConcurrentDictionary<long, long> _userIdToMemberId = new();
    private volatile bool _membersLoaded;

    /// <summary>
    /// True once the member set has been loaded from the database. While false, callers should
    /// fall back to the database rather than treating a cache miss as "not a member".
    /// </summary>
    public bool MembersLoaded => _membersLoaded;

    public int MemberCount => _members.Count;

    public void SetMembers(IEnumerable<PlanetMember> members)
    {
        _members.Clear();
        _userIdToMemberId.Clear();
        foreach (var member in members)
            StoreCore(member);
        _membersLoaded = true;
    }

    /// <summary>
    /// Returns the cached core member (User is null). Read-only: callers must not mutate the
    /// returned instance, and must copy it (attaching a user) before exposing it externally.
    /// </summary>
    public bool TryGetMember(long memberId, out PlanetMember member) =>
        _members.TryGetValue(memberId, out member);

    /// <summary>
    /// Returns the cached core member for a user (User is null). Same read-only contract as
    /// <see cref="TryGetMember"/>.
    /// </summary>
    public bool TryGetMemberByUser(long userId, out PlanetMember member)
    {
        member = null;
        return _userIdToMemberId.TryGetValue(userId, out var memberId) &&
               _members.TryGetValue(memberId, out member);
    }

    public void UpsertMember(PlanetMember member)
    {
        if (member is null)
            return;

        StoreCore(member);
    }

    // Enforces the "core" invariant at the cache boundary: the stored instance never carries a user.
    // Store a copy so callers can safely pass a full member model without having its User nulled.
    private void StoreCore(PlanetMember member)
    {
        var core = member.CopyWithUser(null);
        _members[core.Id] = core;
        _userIdToMemberId[member.UserId] = member.Id;
    }

    public void RemoveMember(long memberId)
    {
        if (_members.TryRemove(memberId, out var removed))
            _userIdToMemberId.TryRemove(removed.UserId, out _);
    }

    /// <summary>
    /// Returns the user ids of all members currently held in the cache. Used to keep referenced
    /// users alive in the node-global user cache.
    /// </summary>
    public long[] GetMemberUserIds() => _userIdToMemberId.Keys.ToArray();

    // Voice Participants //

    private readonly ConcurrentDictionary<long, ConcurrentHashSet<long>> _voiceParticipants = new();

    /// <summary>
    /// True if any voice channel on this planet currently has participants. Used to avoid
    /// unloading a planet that still has active voice sessions.
    /// </summary>
    public bool HasActiveVoiceParticipants()
    {
        foreach (var set in _voiceParticipants.Values)
        {
            if (set.Count > 0)
                return true;
        }
        return false;
    }

    public void AddVoiceParticipant(long channelId, long userId)
    {
        var set = _voiceParticipants.GetOrAdd(channelId, _ => new ConcurrentHashSet<long>());
        set.Add(userId);
    }

    public void RemoveVoiceParticipant(long channelId, long userId)
    {
        if (_voiceParticipants.TryGetValue(channelId, out var set))
        {
            set.Remove(userId);
        }
    }

    public Dictionary<long, List<long>> GetAllVoiceParticipants()
    {
        var result = new Dictionary<long, List<long>>();
        foreach (var kvp in _voiceParticipants)
        {
            var list = kvp.Value.ToList();
            if (list.Count > 0)
                result[kvp.Key] = list;
        }
        return result;
    }

    public void SetVoiceParticipants(long channelId, List<long> userIds)
    {
        if (userIds.Count == 0)
        {
            _voiceParticipants.TryRemove(channelId, out _);
            return;
        }

        var set = _voiceParticipants.GetOrAdd(channelId, _ => new ConcurrentHashSet<long>());
        set.Clear();
        foreach (var userId in userIds)
            set.Add(userId);
    }

    // Channel Inheritance //

    /// <summary>
    /// Gets the inherited-from channel ID for a given channel, or null if not cached.
    /// </summary>
    public bool TryGetInheritanceTarget(long channelId, out long? targetId) =>
        _inheritanceMap.TryGetValue(channelId, out targetId);

    /// <summary>
    /// Sets the inheritance target for a channel.
    /// </summary>
    public void SetInheritanceTarget(long channelId, long targetChannelId)
    {
        _inheritanceMap[channelId] = targetChannelId;

        // Track the inverse relationship
        var inheritorsList = _inheritanceLists.GetOrAdd(targetChannelId, _ => new ConcurrentHashSet<long>());
        inheritorsList.Add(channelId);
    }

    /// <summary>
    /// Gets all channels that inherit from a given channel.
    /// </summary>
    public ConcurrentHashSet<long>? GetInheritors(long channelId)
    {
        _inheritanceLists.TryGetValue(channelId, out var result);
        return result;
    }

    /// <summary>
    /// Clears inheritance cache for a channel.
    /// </summary>
    public void ClearInheritanceCache(long channelId)
    {
        _inheritanceMap.TryRemove(channelId, out _);
        if (_inheritanceLists.TryGetValue(channelId, out var inheritors))
        {
            inheritors.Clear();
        }
    }

    /// <summary>
    /// Clears all channel inheritance cache state for this planet.
    /// </summary>
    public void ClearAllInheritanceCaches()
    {
        _inheritanceMap.Clear();
        _inheritanceLists.Clear();
    }
}
