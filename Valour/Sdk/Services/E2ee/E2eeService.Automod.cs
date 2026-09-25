using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Sdk.Services;

public partial class E2eeService
{
    private readonly SemaphoreSlim _automodLock = new(1, 1);

    /// <summary>
    /// Computes automod term hashes for a planet this account moderates, so
    /// word and command triggers work in its encrypted channels. Does nothing
    /// for members who cannot manage the planet.
    /// </summary>
    public async Task SyncAutomodTermsIfModeratorAsync(long planetId)
    {
        if (Status != E2eeStatus.Ready || !_client.Cache.Planets.TryGet(planetId, out var planet))
            return;

        var member = planet.MyMember;
        if (member is null || !member.HasPermission(PlanetPermissions.Manage))
            return;

        await SyncAutomodTermsAsync(planet);
    }

    public async Task<TaskResult> SyncAutomodTermsAsync(Planet planet)
    {
        await _automodLock.WaitAsync();
        try
        {
            var work = await planet.Node.GetJsonAsync<List<AutomodTermsWorkDto>>(
                $"api/e2ee/planets/{planet.Id}/automod/work", cacheDurationMs: null);
            if (!work.Success)
                return Fail(work.Message);
            if (work.Data is null || work.Data.Count == 0)
                return TaskResult.SuccessResult;

            // In a private planet the server's list of triggers is not
            // trusted: hashing any text it names with a channel's search key
            // would let it test which words appear in messages. Only text an
            // admin approved in the membership log is hashed there.
            var (governed, log) = await GetPlanetGovernanceAsync(planet.Id, planet.Node,
                planet.EncryptionMode == PlanetEncryptionMode.InviteOnly);
            if (governed && log is null)
                return Fail("The planet's membership log could not be verified.");
            if (!governed)
                log = null;

            var uploads = new List<AutomodTermsUploadDto>();
            foreach (var item in work.Data)
            {
                if (!planet.Channels.TryGet(item.ChannelId, out var channel))
                    continue;

                var indexSecret = await GetSecretAsync(channel, item.IndexGeneration);
                if (indexSecret is null)
                    continue;

                foreach (var trigger in item.Triggers)
                {
                    if (log is not null &&
                        !log.IsAutomodTriggerApproved(trigger.TriggerId, trigger.Type, trigger.TriggerWords))
                        continue;

                    uploads.Add(new AutomodTermsUploadDto
                    {
                        TriggerId = trigger.TriggerId,
                        ChannelId = channel.Id,
                        IndexGeneration = item.IndexGeneration,
                        Alternatives = TriggerAlternatives(indexSecret.IndexKey, channel.Id, trigger)
                    });
                }
            }

            foreach (var chunk in uploads.Chunk(100))
            {
                var result = await planet.Node.PostAsync($"api/e2ee/planets/{planet.Id}/automod/terms", chunk.ToList());
                if (!result.Success)
                    return result;
            }

            return TaskResult.SuccessResult;
        }
        finally
        {
            _automodLock.Release();
        }
    }

    /// <summary>
    /// Approves automod triggers in a private planet's membership log so
    /// moderators' apps compute hashes for them. Only admins in the log can
    /// approve; call this when a person saves a trigger, never for triggers
    /// the server lists, since approving is what stops the server from using
    /// automod to learn message words. Does nothing in public planets.
    /// </summary>
    public async Task<TaskResult> ApproveAutomodTriggersAsync(Planet planet, IEnumerable<AutomodTrigger> triggers)
    {
        var (governed, _) = await GetPlanetGovernanceAsync(planet.Id, planet.Node,
            planet.EncryptionMode == PlanetEncryptionMode.InviteOnly);
        if (!governed)
            return TaskResult.SuccessResult;

        var approvals = triggers
            .Where(t => t.Type is AutomodTriggerType.Blacklist or AutomodTriggerType.Command)
            .Select(t => new AutomodApproval(t.Id, AutomodApproval.HashTrigger((int)t.Type, t.TriggerWords)))
            .ToList();
        if (approvals.Count == 0)
            return TaskResult.SuccessResult;

        if (!await IsPlanetAccessAdminAsync(planet))
            return Fail("Only admins in the planet's membership log can approve automod triggers for encrypted channels.");

        var result = await AppendAccessLogAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            AccessLogEntryType.ApproveAutomodTriggers, r => r.With(automodApprovals: approvals));
        if (!result.Success)
            return Fail(result.Message);

        _ = SyncAutomodTermsIfModeratorAsync(planet.Id);
        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// The term sets that make a trigger fire, matching how the server
    /// matched plain text: each listed word as a word, word start, or word end,
    /// and a command as the first word.
    /// </summary>
    private static List<int[]> TriggerAlternatives(byte[] indexKey, long channelId, AutomodTriggerWorkDto trigger)
    {
        var alternatives = new List<int[]>();
        if (string.IsNullOrWhiteSpace(trigger.TriggerWords))
            return alternatives;

        if (trigger.Type == (int)AutomodTriggerType.Command)
        {
            alternatives.Add([SearchTerms.ForTriggerCommand(indexKey, channelId, trigger.TriggerWords.Trim())]);
            return alternatives;
        }

        foreach (var word in trigger.TriggerWords.Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            alternatives.AddRange(SearchTerms.ForTriggerWord(indexKey, channelId, word));

        return alternatives
            .DistinctBy(a => string.Join(',', a))
            .Take(E2eeLimits.MaxAutomodAlternatives)
            .ToList();
    }
}
