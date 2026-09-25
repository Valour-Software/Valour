using Npgsql;
using NpgsqlTypes;
using Valour.Config.Configs;
using Valour.Sdk.E2ee;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// The outcome of one pass of the legacy sealing worker.
/// </summary>
/// <param name="Sealed">Plain-text messages sealed to their channel's key.</param>
/// <param name="Cleared">Plain-text messages in deleted channels whose text was removed.</param>
/// <param name="Finished">True when no plain-text message was left to look at.</param>
public readonly record struct LegacySealResult(int Sealed, int Cleared, bool Finished)
{
    public bool DidWork => Sealed + Cleared > 0;
}

/// <summary>
/// Background upkeep for end-to-end encryption: sealing plain-text history
/// written before encryption, and removing expired proofs, link sessions,
/// and key requests.
/// </summary>
public class E2eeMaintenanceService
{
    private static readonly TimeSpan KeyRequestLifetime = TimeSpan.FromDays(30);

    /// <summary>Channels one sealing pass looks at before returning.</summary>
    private const int MaxChannelsPerPass = 50;

    // The sealing position: channels before or at _channelCursor are done or
    // were passed over during this sweep. Within the channel being sealed,
    // _messageCursor is the last message handled, so a message that cannot
    // be sealed is not retried forever. Only one server seals at a time (the
    // worker holds a database lock), so this position is kept in memory.
    private static long _channelCursor;
    private static long _messageChannel;
    private static long _messageCursor;
    private static readonly SemaphoreSlim PassLock = new(1, 1);

    private readonly ValourDb _db;
    private readonly E2eeServerKeyService _serverKeys;
    private readonly E2eeIdentityService _identity;
    private readonly E2eeMessageService _messages;
    private readonly E2eeChannelKeyService _channelKeys;
    private readonly HostedPlanetService _hostedPlanets;
    private readonly NodeLifecycleService _nodeLifecycle;
    private readonly ChatCacheService _chatCache;
    private readonly ILogger<E2eeMaintenanceService> _logger;

    public E2eeMaintenanceService(ValourDb db, E2eeServerKeyService serverKeys, E2eeIdentityService identity,
        E2eeMessageService messages, E2eeChannelKeyService channelKeys, HostedPlanetService hostedPlanets,
        NodeLifecycleService nodeLifecycle, ChatCacheService chatCache, ILogger<E2eeMaintenanceService> logger)
    {
        _db = db;
        _serverKeys = serverKeys;
        _identity = identity;
        _messages = messages;
        _channelKeys = channelKeys;
        _hostedPlanets = hostedPlanets;
        _nodeLifecycle = nodeLifecycle;
        _chatCache = chatCache;
        _logger = logger;
    }

    /// <summary>
    /// Seals up to <paramref name="batchSize"/> plain-text messages written
    /// before encryption. The server reads each message once to seal it to its
    /// channel's oldest key, creating the channel's first key if it has none,
    /// then keeps only the sealed copy. Messages in deleted channels and
    /// planets lose their text instead, because nobody can read them again.
    ///
    /// The pass stays on one channel until its plain text is gone, then moves
    /// to the next channel in ID order. Channels of planets that another node
    /// hosts, or that are being migrated, are passed over until the next sweep.
    /// </summary>
    public async Task<LegacySealResult> SealLegacyBatchAsync(int batchSize)
    {
        await PassLock.WaitAsync();
        try
        {
            return await SealPassAsync(Math.Max(1, batchSize));
        }
        finally
        {
            PassLock.Release();
        }
    }

    private async Task<LegacySealResult> SealPassAsync(int batchSize)
    {
        var sealedCount = 0;
        var clearedCount = 0;
        var wrapped = false;
        var hostedHere = new Dictionary<long, bool>();

        for (var visited = 0; visited < MaxChannelsPerPass && sealedCount + clearedCount < batchSize; visited++)
        {
            var after = Interlocked.Read(ref _channelCursor);
            var channelId = await _db.Messages.AsNoTracking()
                .Where(m => m.EncryptionVersion == MessageEncryption.None && m.ChannelId > after)
                .MinAsync(m => (long?)m.ChannelId);

            if (channelId is null)
            {
                // Start the sweep over; channels passed over earlier may be
                // hosted here or unlocked now.
                Interlocked.Exchange(ref _channelCursor, 0);
                if (after == 0 || wrapped)
                    return new LegacySealResult(sealedCount, clearedCount, after == 0 && sealedCount + clearedCount == 0);
                wrapped = true;
                continue;
            }

            var remaining = batchSize - sealedCount - clearedCount;
            ChannelOutcome outcome;
            int count;
            try
            {
                (outcome, count) = await SealChannelAsync(channelId.Value, remaining, hostedHere);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // One broken channel must not stop the sweep; it is tried
                // again on the next one.
                _logger.LogError(e, "Could not seal plain-text history in channel {ChannelId}", channelId);
                _db.ChangeTracker.Clear();
                (outcome, count) = (ChannelOutcome.PassedOver, 0);
            }
            if (outcome == ChannelOutcome.Cleared)
                clearedCount += count;
            else
                sealedCount += count;

            // A channel is left once it is drained or cannot be sealed here.
            if (outcome != ChannelOutcome.MoreRemaining)
            {
                Interlocked.Exchange(ref _channelCursor, channelId.Value);
                Interlocked.Exchange(ref _messageCursor, 0);
            }
        }

        return new LegacySealResult(sealedCount, clearedCount, false);
    }

    private enum ChannelOutcome
    {
        /// <summary>The channel still has plain text for the next batch.</summary>
        MoreRemaining,

        /// <summary>The channel's plain text is sealed.</summary>
        Drained,

        /// <summary>The channel is deleted and its plain text was removed.</summary>
        Cleared,

        /// <summary>The channel belongs to another node or a planet being migrated.</summary>
        PassedOver
    }

    private async Task<(ChannelOutcome Outcome, int Count)> SealChannelAsync(long channelId, int limit,
        Dictionary<long, bool> hostedHere)
    {
        var dbChannel = await _db.Channels.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == channelId);
        Valour.Database.Planet dbPlanet = null;
        if (dbChannel?.PlanetId is { } channelPlanetId)
        {
            dbPlanet = await _db.Planets.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == channelPlanetId);
        }

        var deleted = dbChannel is null || dbChannel.IsDeleted ||
                      (dbChannel.PlanetId is not null && (dbPlanet is null || dbPlanet.IsDeleted));

        if (!deleted && dbPlanet is not null)
        {
            // A planet being migrated is read-only until its copy is handed
            // off, and planet channels are sealed by the node that hosts the
            // planet, which is the one that knows who can view them.
            if (dbPlanet.LockedForMigration || !await IsHostedHereAsync(dbPlanet.Id, hostedHere))
                return (ChannelOutcome.PassedOver, 0);
        }

        var messageCursor = Interlocked.Read(ref _messageChannel) == channelId ? Interlocked.Read(ref _messageCursor) : 0;
        var messages = await _db.Messages.AsNoTracking()
            .Include(x => x.Attachments)
            .Where(x => x.ChannelId == channelId && x.EncryptionVersion == MessageEncryption.None && x.Id > messageCursor)
            .OrderBy(x => x.Id)
            .Take(limit)
            .ToListAsync();

        if (messages.Count > 0)
        {
            await PreserveReportedTextAsync(messages);

            var changed = deleted
                ? await ClearBatchAsync(messages)
                : await SealBatchAsync(dbChannel.ToModel(), messages);

            Interlocked.Exchange(ref _messageChannel, channelId);
            Interlocked.Exchange(ref _messageCursor, messages[^1].Id);
            _chatCache.ForgetChannelMessages(channelId);

            if (messages.Count == limit)
                return (ChannelOutcome.MoreRemaining, changed);

            await ClearNotificationTextAsync(channelId);
            return (deleted ? ChannelOutcome.Cleared : ChannelOutcome.Drained, changed);
        }

        await ClearNotificationTextAsync(channelId);
        return (deleted ? ChannelOutcome.Cleared : ChannelOutcome.Drained, 0);
    }

    /// <summary>
    /// True when this node hosts the planet. A planet assigned to another live
    /// node is passed over after a lookup in the shared node registry; an
    /// unassigned planet is taken on, since its history cannot be sealed
    /// otherwise.
    /// </summary>
    private async Task<bool> IsHostedHereAsync(long planetId, Dictionary<long, bool> memo)
    {
        if (memo.TryGetValue(planetId, out var known))
            return known;

        var hosted = _hostedPlanets.IsHosted(planetId);
        if (!hosted && await _nodeLifecycle.GetActiveNodeForPlanetAsync(planetId) == _nodeLifecycle.Name)
            hosted = (await _hostedPlanets.TryGetAsync(planetId)).HostedPlanet is not null;

        memo[planetId] = hosted;
        return hosted;
    }

    /// <summary>
    /// Seals a batch into the channel's first key generation, so a planet
    /// that hides history from new members does not reveal it through a newer
    /// key. The attachment rows that held embeds are removed in the same
    /// transaction, since the sealed copy carries the embeds. A sealed message
    /// holds at most <see cref="ServerSealedPayload.MaxEmbeds"/> embeds, so a
    /// message with more keeps its first ones and loses the rest.
    /// </summary>
    private async Task<int> SealBatchAsync(Channel channel, List<Valour.Database.Message> messages)
    {
        var generation = await _channelKeys.EnsureSealingGenerationAsync(channel);
        if (generation.Generation > 1)
            generation = await _channelKeys.GetGenerationAsync(channel.Id, 1) ?? generation;

        // Recorded before any message uses the key, so the key is never handed
        // out as an unused one while it protects text.
        await _channelKeys.MarkGenerationUsedAsync(channel.Id, generation.Generation);

        var (keyId, sign) = await _serverKeys.GetSignerAsync();
        var ids = new List<long>();
        var contents = new List<string>();
        var envelopes = new List<byte[]>();

        foreach (var message in messages)
        {
            var embeds = message.Attachments?
                .Where(a => a.Type == MessageAttachmentType.Embed && !string.IsNullOrWhiteSpace(a.Data))
                .OrderBy(a => a.SortOrder)
                .Select(a => a.Data)
                .Take(ServerSealedPayload.MaxEmbeds)
                .ToList();

            byte[] envelope;
            try
            {
                envelope = ServerSealing.Seal(generation.SealPublicKey, channel.Id, channel.PlanetId ?? 0,
                    generation.Generation, ServerSealedKind.Legacy, message.AuthorUserId,
                    new DateTimeOffset(DateTime.SpecifyKind(message.TimeSent, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                    message.Content ?? string.Empty, embeds is { Count: > 0 } ? embeds : null, keyId, sign);
            }
            catch (Exception e) when (e is E2eeFormatException or ArgumentException)
            {
                _logger.LogWarning(e, "Could not seal plain-text message {MessageId}", message.Id);
                continue;
            }

            ids.Add(message.Id);
            contents.Add(message.Content);
            envelopes.Add(envelope);
        }

        if (ids.Count == 0)
            return 0;

        // One statement replaces the whole batch. A message is replaced only
        // if it is still plain text and unchanged since it was read, so a
        // concurrent edit is never overwritten.
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var sealedIds = await _db.Database.SqlQueryRaw<long>(
            """
            UPDATE messages AS m
            SET content = '', encryption_version = @sealed, envelope = t.envelope,
                key_generation = @generation, search_terms = NULL
            FROM unnest(@ids, @contents, @envelopes) AS t(id, content, envelope)
            WHERE m.id = t.id AND m.encryption_version = @plain AND m.content IS NOT DISTINCT FROM t.content
            RETURNING m.id AS "Value"
            """,
            new NpgsqlParameter<int>("sealed", MessageEncryption.ServerSealed),
            new NpgsqlParameter<int>("plain", MessageEncryption.None),
            new NpgsqlParameter<int>("generation", generation.Generation),
            new NpgsqlParameter<long[]>("ids", ids.ToArray()),
            new NpgsqlParameter("contents", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = contents.ToArray() },
            new NpgsqlParameter("envelopes", NpgsqlDbType.Array | NpgsqlDbType.Bytea) { Value = envelopes.ToArray() })
            .ToListAsync();

        await DeleteEmbedAttachmentsAsync(messages, sealedIds);
        await transaction.CommitAsync();
        return sealedIds.Count;
    }

    /// <summary>
    /// Removes the text of plain-text messages in a deleted channel or planet.
    /// They are marked as server-sealed without an envelope so they are not
    /// visited again.
    /// </summary>
    private async Task<int> ClearBatchAsync(List<Valour.Database.Message> messages)
    {
        var ids = messages.Select(m => m.Id).ToList();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var cleared = await _db.Messages
            .Where(x => ids.Contains(x.Id) && x.EncryptionVersion == MessageEncryption.None)
            .ExecuteUpdateAsync(x => x
                .SetProperty(m => m.Content, string.Empty)
                .SetProperty(m => m.EncryptionVersion, MessageEncryption.ServerSealed)
                .SetProperty(m => m.Envelope, (byte[])null)
                .SetProperty(m => m.SearchTerms, (int[])null));
        await DeleteEmbedAttachmentsAsync(messages, ids);
        await transaction.CommitAsync();
        return cleared;
    }

    private async Task DeleteEmbedAttachmentsAsync(List<Valour.Database.Message> messages, List<long> changedIds)
    {
        var changed = changedIds.ToHashSet();
        var embedIds = messages
            .Where(m => changed.Contains(m.Id))
            .SelectMany(m => m.Attachments ?? [])
            .Where(a => a.Type == MessageAttachmentType.Embed)
            .Select(a => a.Id)
            .ToList();
        if (embedIds.Count > 0)
            await _db.MessageAttachments.Where(a => embedIds.Contains(a.Id)).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Copies the text of messages that open reports point to into the
    /// reports' evidence before the text leaves the database, so moderators
    /// can still read what was reported. The server read the text from its
    /// own records, so the evidence counts as server attested.
    /// </summary>
    private async Task PreserveReportedTextAsync(List<Valour.Database.Message> messages)
    {
        var ids = messages.Select(m => m.Id).ToList();
        var reports = await _db.Reports.AsNoTracking()
            .Where(r => !r.Reviewed && r.MessageId != null && ids.Contains(r.MessageId.Value))
            .Select(r => new { r.Id, MessageId = r.MessageId.Value })
            .ToListAsync();
        var planetReports = await _db.PlanetReports.AsNoTracking()
            .Where(r => !r.Reviewed && r.MessageId != null && ids.Contains(r.MessageId.Value))
            .Select(r => new { r.Id, MessageId = r.MessageId.Value })
            .ToListAsync();
        if (reports.Count == 0 && planetReports.Count == 0)
            return;

        var existing = await _db.ReportEvidenceEntries.AsNoTracking()
            .Where(e => ids.Contains(e.MessageId))
            .Select(e => new { e.ReportId, e.PlanetReportId, e.MessageId })
            .ToListAsync();
        var byId = messages.ToDictionary(m => m.Id);
        var now = DateTime.UtcNow;

        Valour.Database.ReportEvidence Evidence(long messageId, string reportId, long? planetReportId)
        {
            var message = byId[messageId];
            return new Valour.Database.ReportEvidence
            {
                ReportId = reportId,
                PlanetReportId = planetReportId,
                MessageId = messageId,
                Revision = 0,
                ChannelId = message.ChannelId,
                AuthorUserId = message.AuthorUserId,
                TimeSent = message.TimeSent,
                Content = message.Content,
                Embed = message.Attachments?
                    .Where(a => a.Type == MessageAttachmentType.Embed)
                    .OrderBy(a => a.SortOrder)
                    .FirstOrDefault()?.Data,
                Verification = (int)ReportEvidenceVerification.ServerAttested,
                CreatedAt = now
            };
        }

        foreach (var report in reports.Where(r => !existing.Any(e => e.ReportId == r.Id && e.MessageId == r.MessageId)))
            _db.ReportEvidenceEntries.Add(Evidence(report.MessageId, report.Id, null));
        foreach (var report in planetReports.Where(r =>
                     !existing.Any(e => e.PlanetReportId == r.Id && e.MessageId == r.MessageId)))
            _db.ReportEvidenceEntries.Add(Evidence(report.MessageId, null, report.Id));

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    /// <summary>Replaces message text copied into a channel's notifications.</summary>
    private Task ClearNotificationTextAsync(long channelId) =>
        _db.Notifications
            .Where(n => n.ChannelId == channelId && n.Body != NotificationService.EncryptedMessageBody &&
                        (n.Source == NotificationSource.DirectMessage || n.Source == NotificationSource.DirectReply ||
                         n.Source == NotificationSource.DirectMention ||
                         n.Source == NotificationSource.PlanetMemberReply ||
                         n.Source == NotificationSource.PlanetMemberMention ||
                         n.Source == NotificationSource.PlanetRoleMention ||
                         n.Source == NotificationSource.PlanetHereMention ||
                         n.Source == NotificationSource.PlanetEveryoneMention ||
                         n.Source == NotificationSource.ChannelActivity))
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.Body, NotificationService.EncryptedMessageBody));

    public async Task CleanupAsync()
    {
        var retention = TimeSpan.FromDays(Math.Max(1, E2eeConfig.Current.ProofRetentionDays));
        var proofs = await _messages.DeleteExpiredProofsAsync(retention);
        var sessions = await _identity.DeleteExpiredLinkSessionsAsync();
        var cutoff = DateTime.UtcNow - KeyRequestLifetime;
        var requests = await _db.E2eeKeyRequests.Where(x => x.RequestedAt < cutoff).ExecuteDeleteAsync();

        if (proofs + sessions + requests > 0)
        {
            _logger.LogInformation(
                "Removed {Proofs} expired message proofs, {Sessions} device link sessions, and {Requests} stale key requests",
                proofs, sessions, requests);
        }
    }
}
