#nullable enable annotations

using Npgsql;
using Valour.Sdk.E2ee;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Validation and storage rules for encrypted messages.
///
/// The server cannot read end-to-end encrypted messages. It checks what it
/// can see: the signed header routes to the right channel and author, the
/// signing device is active, the envelope uses the channel's newest key, and
/// the uploaded search terms match the hash the author signed.
/// </summary>
public class E2eeMessageService
{
    // A server-sealed webhook message may carry five embeds of up to 64 KB each.
    private const int MaxEvidenceEmbedLength = 5 * 64 * 1024 + 1024;

    private const int MaxEnvelopeSize = 24 * 1024;
    private const int MaxMentions = 50;

    private readonly ValourDb _db;
    private readonly E2eeIdentityService _identity;
    private readonly E2eeChannelKeyService _channelKeys;
    private readonly E2eeServerKeyService _serverKeys;

    public E2eeMessageService(ValourDb db, E2eeIdentityService identity, E2eeChannelKeyService channelKeys,
        E2eeServerKeyService serverKeys)
    {
        _db = db;
        _identity = identity;
        _channelKeys = channelKeys;
        _serverKeys = serverKeys;
    }

    /// <summary>
    /// Validates a new end-to-end encrypted message and moves its search terms
    /// to the server-only field so they are stored but never relayed.
    /// </summary>
    public async Task<TaskResult> ValidateNewAsync(Message message, Channel channel)
    {
        var headerResult = await ValidateEnvelopeAsync(message, channel);
        if (!headerResult.Success)
            return TaskResult.FromFailure(headerResult.Message);

        var header = headerResult.Data;
        if (header.Revision != 0)
            return TaskResult.FromFailure("A new message must be revision zero.");
        if (header.ReplyToId != (message.ReplyToId ?? 0))
            return TaskResult.FromFailure("The encrypted reply does not match the message.");

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates an encrypted edit. The edit keeps the message's nonce and
    /// advances its revision, so earlier revisions stay provable.
    /// </summary>
    public async Task<TaskResult> ValidateEditAsync(Message updated, ISharedMessage old, Channel channel)
    {
        var headerResult = await ValidateEnvelopeAsync(updated, channel);
        if (!headerResult.Success)
            return TaskResult.FromFailure(headerResult.Message);

        var header = headerResult.Data;
        if (header.ReplyToId != (old.ReplyToId ?? 0))
            return TaskResult.FromFailure("An edit cannot change the message it replies to.");

        if (old.EncryptionVersion == MessageEncryption.EndToEnd)
        {
            var (_, oldHeader) = MessageCrypto.ReadHeader(old.Envelope);
            if (!oldHeader.MessageNonce.SequenceEqual(header.MessageNonce))
                return TaskResult.FromFailure("An edit must keep the message's nonce.");
            if (header.Revision != oldHeader.Revision + 1)
                return TaskResult.FromFailure("An edit must advance the revision by one.");
        }

        return TaskResult.SuccessResult;
    }

    private async Task<TaskResult<MessageEnvelopeHeader>> ValidateEnvelopeAsync(Message message, Channel channel)
    {
        if (message.Envelope is null || message.Envelope.Length > MaxEnvelopeSize)
            return TaskResult<MessageEnvelopeHeader>.FromFailure("Invalid encrypted message.");
        if (!string.IsNullOrEmpty(message.Content))
            return TaskResult<MessageEnvelopeHeader>.FromFailure("Encrypted messages cannot include plain text.");

        MessageEnvelope envelope;
        MessageEnvelopeHeader header;
        try
        {
            (envelope, header) = MessageCrypto.ReadHeader(message.Envelope);
        }
        catch (E2eeFormatException e)
        {
            return TaskResult<MessageEnvelopeHeader>.FromFailure(e.Message);
        }

        if (header.ChannelId != channel.Id || header.PlanetId != (channel.PlanetId ?? 0) ||
            header.AuthorUserId != message.AuthorUserId)
            return TaskResult<MessageEnvelopeHeader>.FromFailure("The encrypted message does not match its channel or author.");

        var latest = await _channelKeys.GetLatestGenerationAsync(channel.Id);
        if (latest == 0)
            return TaskResult<MessageEnvelopeHeader>.FromFailure("This channel is not encrypted yet.");
        if (header.Generation != latest)
            return TaskResult<MessageEnvelopeHeader>.FromFailure($"{E2eeErrorCodes.StaleGeneration}: The channel has a newer key.");
        if (await _channelKeys.IsRotationRequiredAsync(channel, latest))
            return TaskResult<MessageEnvelopeHeader>.FromFailure($"{E2eeErrorCodes.RotationRequired}: The channel key must be replaced.");

        var author = await _identity.GetStateAsync(message.AuthorUserId);
        var device = author?.GetActiveSigner(header.AuthorDeviceId);
        if (device is null || header.AuthorDeviceId.StartsWith(RecoveryKey.IdPrefix, StringComparison.Ordinal))
            return TaskResult<MessageEnvelopeHeader>.FromFailure("The sending device is not active.");
        if (!MessageEnvelope.VerifySignature(envelope.Header, envelope.BodyHash(), envelope.Signature, device.SignPublicKey))
            return TaskResult<MessageEnvelopeHeader>.FromFailure("The message signature is invalid.");

        var terms = SearchTerms.Normalize(message.SearchTerms ?? []);
        if (terms.Length > SearchTerms.MaxTerms)
            return TaskResult<MessageEnvelopeHeader>.FromFailure("Too many search terms.");
        if (!E2eeCrypto.FixedTimeEquals(SearchTerms.Hash(terms), header.TermsHash))
            return TaskResult<MessageEnvelopeHeader>.FromFailure("The search terms do not match the message.");

        message.KeyGeneration = header.Generation;
        message.IndexedTerms = terms;
        message.SearchTerms = null;
        await _channelKeys.MarkGenerationUsedAsync(channel.Id, header.Generation);
        return TaskResult<MessageEnvelopeHeader>.FromData(header);
    }

    /// <summary>
    /// Keeps only well-formed, distinct mentions from an encrypted message's
    /// metadata. The server cannot parse the text, so the sender lists them.
    /// </summary>
    public static List<Mention>? SanitizeMentions(List<Mention>? mentions)
    {
        if (mentions is null || mentions.Count == 0)
            return null;

        return mentions
            .Where(m => m is not null && Enum.IsDefined(m.Type) && m.TargetId > 0)
            .DistinctBy(m => (m.Type, m.TargetId))
            .Take(MaxMentions)
            .Select(m => new Mention { Type = m.Type, TargetId = m.TargetId })
            .ToList();
    }

    /// <summary>
    /// Keeps only http and https URLs the sender asked to preview. A URL the
    /// sender wrote inside a spoiler arrives as ||url|| and keeps the markers,
    /// so the preview built from the joined list is marked as a spoiler.
    /// </summary>
    public static List<string> SanitizePreviewUrls(List<string>? urls)
    {
        if (urls is null)
            return [];

        return urls
            .Where(u => u is not null)
            .Select(u => u.Length > 4 && u.StartsWith("||", StringComparison.Ordinal) &&
                         u.EndsWith("||", StringComparison.Ordinal)
                ? (Url: u[2..^2], Spoiler: true)
                : (Url: u, Spoiler: false))
            .Where(x => Uri.TryCreate(x.Url, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
                        x.Url.Length <= 2048 && !x.Url.Contains('|'))
            .Select(x => x.Spoiler ? $"||{x.Url}||" : x.Url)
            .Distinct()
            .Take(E2eeLimits.MaxPreviewUrls)
            .ToList();
    }

    // Server-written messages

    /// <summary>
    /// Seals a message the server writes, such as a webhook post, to the
    /// channel's newest key, creating the channel's first key if it has none.
    /// The message must already have its time, which the attestation covers.
    /// </summary>
    public async Task SealAsync(Message message, Channel channel, ServerSealedKind kind,
        IReadOnlyList<string> embeds = null)
    {
        var generation = await _channelKeys.EnsureSealingGenerationAsync(channel);
        var (keyId, sign) = await _serverKeys.GetSignerAsync();
        message.Envelope = ServerSealing.Seal(generation.SealPublicKey, message.ChannelId, message.PlanetId ?? 0,
            generation.Generation, kind, message.AuthorUserId,
            new DateTimeOffset(DateTime.SpecifyKind(message.TimeSent, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            message.Content, embeds, keyId, sign);
        message.EncryptionVersion = MessageEncryption.ServerSealed;
        message.KeyGeneration = generation.Generation;
        message.Content = string.Empty;
        await _channelKeys.MarkGenerationUsedAsync(channel.Id, generation.Generation);
    }

    // Proofs

    /// <summary>
    /// Records proof of an encrypted revision before it is replaced or deleted.
    /// </summary>
    public async Task SaveProofAsync(ISharedMessage message)
    {
        var proof = BuildProof(message);
        if (proof is null)
            return;

        // Two requests can record the same revision at once, for example an
        // edit racing a delete; the first proof is kept.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO message_proofs (message_id, revision, channel_id, planet_id, author_user_id,
                 encryption_version, header, body_hash, signature, time_sent, created_at)
             VALUES ({proof.MessageId}, {proof.Revision}, {proof.ChannelId}, {proof.PlanetId}, {proof.AuthorUserId},
                 {proof.EncryptionVersion}, {proof.Header}, {proof.BodyHash}, {proof.Signature},
                 {DateTime.SpecifyKind(proof.TimeSent, DateTimeKind.Utc)}, {proof.CreatedAt})
             ON CONFLICT DO NOTHING
             """);
    }

    private static Valour.Database.MessageProof? BuildProof(ISharedMessage message)
    {
        if (message.Envelope is null)
            return null;

        if (message.EncryptionVersion == MessageEncryption.EndToEnd)
        {
            var (envelope, header) = MessageCrypto.ReadHeader(message.Envelope);
            return new Valour.Database.MessageProof
            {
                MessageId = message.Id,
                Revision = header.Revision,
                ChannelId = message.ChannelId,
                PlanetId = message.PlanetId,
                AuthorUserId = message.AuthorUserId,
                EncryptionVersion = message.EncryptionVersion,
                Header = envelope.Header,
                BodyHash = envelope.BodyHash(),
                Signature = envelope.Signature,
                TimeSent = message.TimeSent,
                CreatedAt = DateTime.UtcNow
            };
        }

        if (message.EncryptionVersion == MessageEncryption.ServerSealed)
        {
            var (_, headerBytes, _) = ServerSealing.Read(message.Envelope);
            return new Valour.Database.MessageProof
            {
                MessageId = message.Id,
                Revision = 0,
                ChannelId = message.ChannelId,
                PlanetId = message.PlanetId,
                AuthorUserId = message.AuthorUserId,
                EncryptionVersion = message.EncryptionVersion,
                Header = headerBytes,
                TimeSent = message.TimeSent,
                CreatedAt = DateTime.UtcNow
            };
        }

        return null;
    }

    public async Task<int> DeleteExpiredProofsAsync(TimeSpan retention)
    {
        var cutoff = DateTime.UtcNow - retention;

        // Proofs attached to reports stay as long as the report's evidence does.
        var reported = _db.ReportEvidenceEntries.Select(x => x.MessageId);
        return await _db.MessageProofs
            .Where(x => x.CreatedAt < cutoff && !reported.Contains(x.MessageId))
            .ExecuteDeleteAsync();
    }

    // Report evidence

    /// <summary>
    /// Checks message text a reporter revealed. Text is stored either way so
    /// moderators can read it, but only verified text is marked as proven.
    /// </summary>
    public async Task<TaskResult<List<Valour.Database.ReportEvidence>>> VerifyEvidenceAsync(
        IReadOnlyList<MessageEvidenceDto>? evidence, long reporterUserId)
    {
        var results = new List<Valour.Database.ReportEvidence>();
        if (evidence is null || evidence.Count == 0)
            return TaskResult<List<Valour.Database.ReportEvidence>>.FromData(results);
        if (evidence.Count > 25)
            return TaskResult<List<Valour.Database.ReportEvidence>>.FromFailure("Reports can include up to 25 messages.");

        foreach (var item in evidence.DistinctBy(e => (e.MessageId, e.Revision)))
        {
            if ((item.Content?.Length ?? 0) > MessageCrypto.MaxContentLength || (item.Embed?.Length ?? 0) > MaxEvidenceEmbedLength)
                return TaskResult<List<Valour.Database.ReportEvidence>>.FromFailure("Revealed message is too long.");

            var record = await LoadRecordAsync(item.MessageId, item.Revision);
            if (record is null)
                return TaskResult<List<Valour.Database.ReportEvidence>>.FromFailure("A reported message no longer has proof on the server.");

            var channel = await _db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == record.ChannelId);
            if (channel is null || !await _channelKeys.CanViewAsync(channel.ToModel(), reporterUserId))
                return TaskResult<List<Valour.Database.ReportEvidence>>.FromFailure("You can only report messages in channels you can view.");

            results.Add(new Valour.Database.ReportEvidence
            {
                MessageId = item.MessageId,
                Revision = item.Revision,
                ChannelId = record.ChannelId,
                AuthorUserId = record.AuthorUserId,
                TimeSent = record.TimeSent,
                Content = item.Content,
                Embed = item.Embed,
                Verification = (int)await VerifyAsync(record, item),
                CreatedAt = DateTime.UtcNow
            });
        }

        return TaskResult<List<Valour.Database.ReportEvidence>>.FromData(results);
    }

    private sealed class EvidenceRecord
    {
        public long ChannelId { get; init; }
        public long AuthorUserId { get; init; }
        public DateTime TimeSent { get; init; }
        public int EncryptionVersion { get; init; }
        public byte[] Header { get; init; }
        public byte[] BodyHash { get; init; }
        public byte[] Signature { get; init; }
    }

    /// <summary>
    /// Finds the reported revision: the live message if it is still at that
    /// revision, otherwise a proof kept after an edit or delete.
    /// </summary>
    private async Task<EvidenceRecord?> LoadRecordAsync(long messageId, int revision)
    {
        var live = await _db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId);
        var liveModel = live?.ToModel() ?? PlanetMessageWorker.GetStagedMessage(messageId);

        if (liveModel is not null)
        {
            var proof = BuildProof(liveModel);
            if (proof is not null && proof.Revision == revision)
            {
                return new EvidenceRecord
                {
                    ChannelId = liveModel.ChannelId,
                    AuthorUserId = liveModel.AuthorUserId,
                    TimeSent = liveModel.TimeSent,
                    EncryptionVersion = liveModel.EncryptionVersion,
                    Header = proof.Header,
                    BodyHash = proof.BodyHash,
                    Signature = proof.Signature
                };
            }
        }

        var stored = await _db.MessageProofs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.MessageId == messageId && x.Revision == revision);
        if (stored is null)
            return null;

        return new EvidenceRecord
        {
            ChannelId = stored.ChannelId,
            AuthorUserId = stored.AuthorUserId,
            TimeSent = stored.TimeSent,
            EncryptionVersion = stored.EncryptionVersion,
            Header = stored.Header,
            BodyHash = stored.BodyHash,
            Signature = stored.Signature
        };
    }

    private async Task<ReportEvidenceVerification> VerifyAsync(EvidenceRecord record, MessageEvidenceDto item)
    {
        try
        {
            switch (record.EncryptionVersion)
            {
                case MessageEncryption.EndToEnd:
                {
                    var header = MessageEnvelopeHeader.Decode(record.Header);
                    if (!Franking.Verify(header, item.FrankingKey, item.Content, item.Embed))
                        return ReportEvidenceVerification.Unverified;

                    var author = await _identity.GetStateAsync(record.AuthorUserId);
                    var device = author?.GetDeviceAt(header.AuthorDeviceId, record.TimeSent);
                    if (device is null ||
                        !MessageEnvelope.VerifySignature(record.Header, record.BodyHash, record.Signature, device.SignPublicKey))
                        return ReportEvidenceVerification.Unverified;

                    return ReportEvidenceVerification.AuthorSigned;
                }

                case MessageEncryption.ServerSealed:
                {
                    var header = ServerSealedHeader.Decode(record.Header);
                    var serverKey = await _serverKeys.GetPublicKeyAsync(header.ServerKeyId);
                    return ServerSealing.VerifyAttestation(header, item.Salt, item.Content, item.Embed, serverKey)
                        ? ReportEvidenceVerification.ServerAttested
                        : ReportEvidenceVerification.Unverified;
                }

                default:
                    return ReportEvidenceVerification.Unverified;
            }
        }
        catch (Exception e) when (e is E2eeFormatException or E2eeVerificationException)
        {
            return ReportEvidenceVerification.Unverified;
        }
    }

    public async Task SaveEvidenceAsync(IEnumerable<Valour.Database.ReportEvidence> evidence, string reportId,
        long? planetReportId)
    {
        foreach (var item in evidence)
        {
            item.ReportId = reportId;
            item.PlanetReportId = planetReportId;
            _db.ReportEvidenceEntries.Add(item);
        }
        await _db.SaveChangesAsync();
    }

    public async Task<List<ReportEvidenceDto>> GetEvidenceAsync(string reportId, long? planetReportId)
    {
        var query = _db.ReportEvidenceEntries.AsNoTracking();
        query = reportId is not null
            ? query.Where(x => x.ReportId == reportId)
            : query.Where(x => x.PlanetReportId == planetReportId);

        return await query.OrderBy(x => x.TimeSent).Select(x => new ReportEvidenceDto
        {
            MessageId = x.MessageId,
            Revision = x.Revision,
            ChannelId = x.ChannelId,
            AuthorUserId = x.AuthorUserId,
            TimeSent = x.TimeSent,
            Content = x.Content,
            Embed = x.Embed,
            Verification = (ReportEvidenceVerification)x.Verification
        }).ToListAsync();
    }

    // Search

    /// <summary>
    /// Finds candidate messages whose terms contain every term of any given
    /// set. Hashes are truncated, so the caller confirms matches after
    /// decrypting.
    /// </summary>
    public async Task<List<Message>> SearchAsync(Channel channel, EncryptedSearchRequest request)
    {
        var count = Math.Clamp(request.Count, 1, E2eeLimits.MaxSearchResults);
        var before = request.BeforeId ?? long.MaxValue;

        // Each term set is one search key's hashes of the query. A message
        // matches when it contains every term of any set, found in one query.
        var sets = (request.TermSets ?? []).Take(E2eeLimits.MaxSearchTermSets)
            .Select(set => SearchTerms.Normalize(set ?? []))
            .Where(terms => terms.Length is > 0 and <= E2eeLimits.MaxTermsPerSearchSet)
            .ToList();
        if (sets.Count == 0)
            return [];

        var parameters = new List<object>
        {
            new NpgsqlParameter<long>("channel_id", channel.Id),
            new NpgsqlParameter<long>("before", before),
            new NpgsqlParameter<int>("count", count)
        };
        var conditions = new List<string>();
        for (var i = 0; i < sets.Count; i++)
        {
            parameters.Add(new NpgsqlParameter<int[]>($"terms{i}", sets[i]));
            conditions.Add($"search_terms @> @terms{i}");
        }

        // Only parameter names are placed in the SQL; every value is a parameter.
        var sql = $"""
                   SELECT id AS "Value" FROM messages
                   WHERE channel_id = @channel_id AND id < @before AND ({string.Join(" OR ", conditions)})
                   ORDER BY id DESC LIMIT @count
                   """;
        var ids = await _db.Database.SqlQueryRaw<long>(sql, parameters.ToArray()).ToListAsync();
        if (ids.Count == 0)
            return [];

        return await _db.Messages.AsSplitQuery().AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Include(x => x.ReplyToMessage).ThenInclude(x => x.Attachments)
            .Include(x => x.ReplyToMessage).ThenInclude(x => x.Mentions)
            .Include(x => x.ReplyToMessage).ThenInclude(x => x.Reactions)
            .Include(x => x.Reactions)
            .Include(x => x.Attachments)
            .Include(x => x.Mentions)
            .OrderByDescending(x => x.Id)
            .Select(x => x.ToModel())
            .ToListAsync();
    }

    /// <summary>
    /// Server-sealed messages have no terms until a member who can read them
    /// computes and uploads them. Returns the newest ones.
    /// </summary>
    public async Task<List<Message>> GetUnindexedAsync(Channel channel, int count)
    {
        count = Math.Clamp(count, 1, E2eeLimits.MaxUnindexedBatch);

        // The encryption version is written into the SQL as a constant rather
        // than passed as a parameter, so the planner can match the partial
        // index ix_messages_unindexed_sealed on (channel_id, id) WHERE
        // encryption_version = 2 AND search_terms IS NULL and read the newest
        // rows from it without scanning the channel.
        var sql = $"""
                   SELECT id AS "Value" FROM messages
                   WHERE channel_id = @channel_id
                     AND encryption_version = {MessageEncryption.ServerSealed}
                     AND search_terms IS NULL
                   ORDER BY id DESC LIMIT @count
                   """;
        var ids = await _db.Database.SqlQueryRaw<long>(sql,
                new NpgsqlParameter<long>("channel_id", channel.Id),
                new NpgsqlParameter<int>("count", count))
            .ToListAsync();
        if (ids.Count == 0)
            return [];

        return await _db.Messages.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .OrderByDescending(x => x.Id)
            .Select(x => x.ToModel())
            .ToListAsync();
    }

    public async Task<TaskResult> SetSealedTermsAsync(Channel channel, List<MessageTermsDto>? items)
    {
        if (items is null || items.Count == 0)
            return TaskResult.SuccessResult;
        if (items.Count > E2eeLimits.MaxUnindexedBatch)
            return TaskResult.FromFailure("Too many messages.");

        foreach (var item in items)
        {
            if (item is null)
                return TaskResult.FromFailure("Include the message and its terms.");

            var terms = SearchTerms.Normalize(item.Terms ?? []);
            if (terms.Length > SearchTerms.MaxTerms)
                return TaskResult.FromFailure("Too many search terms.");

            await _db.Messages
                .Where(x => x.Id == item.MessageId && x.ChannelId == channel.Id &&
                            x.EncryptionVersion == MessageEncryption.ServerSealed && x.SearchTerms == null)
                .ExecuteUpdateAsync(x => x.SetProperty(m => m.SearchTerms, terms));
        }

        return TaskResult.SuccessResult;
    }
}
