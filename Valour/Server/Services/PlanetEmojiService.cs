using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Server.Services;

public class PlanetEmojiService
{
    private readonly ValourDb _db;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<PlanetEmojiService> _logger;

    public PlanetEmojiService(
        ValourDb db,
        HostedPlanetService hostedPlanetService,
        CoreHubService coreHub,
        ILogger<PlanetEmojiService> logger)
    {
        _db = db;
        _hostedPlanetService = hostedPlanetService;
        _coreHub = coreHub;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlanetEmoji>> GetAllAsync(long planetId)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hosted.Emojis.List;
    }

    public async Task<PlanetEmoji?> GetAsync(long planetId, long emojiId)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hosted.GetEmoji(emojiId);
    }

    public async Task<TaskResult<PlanetEmoji>> CreateAsync(
        long planetId,
        long creatorUserId,
        string name,
        bool notify = true)
    {
        var normalizedName = PlanetEmojiText.NormalizeName(name);
        if (!PlanetEmojiText.IsValidName(normalizedName))
        {
            return TaskResult<PlanetEmoji>.FromFailure(
                "Emoji name must be 2-32 characters and use only lowercase letters, numbers, or underscores.");
        }

        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);

        var count = hosted.Emojis.List.Count;
        if (count >= ISharedPlanetEmoji.MaxPerPlanet)
        {
            return TaskResult<PlanetEmoji>.FromFailure(
                $"Planets can have up to {ISharedPlanetEmoji.MaxPerPlanet} custom emojis.");
        }

        if (hosted.Emojis.List.Any(x => x.Name == normalizedName))
        {
            return TaskResult<PlanetEmoji>.FromFailure(
                "An emoji with that name already exists on this planet.");
        }

        var model = new PlanetEmoji
        {
            Id = IdManager.Generate(),
            PlanetId = planetId,
            CreatorUserId = creatorUserId,
            Name = normalizedName,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _db.PlanetEmojis.AddAsync(model.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create planet emoji {Name} on planet {PlanetId}", normalizedName, planetId);
            return TaskResult<PlanetEmoji>.FromFailure("Failed to create emoji.");
        }

        hosted.UpsertEmoji(model);

        if (notify)
            _coreHub.NotifyPlanetItemChange(model);

        return TaskResult<PlanetEmoji>.FromData(model);
    }

    public async Task<TaskResult> DeleteAsync(long planetId, long emojiId, bool notify = true)
    {
        var dbEmoji = await _db.PlanetEmojis
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.Id == emojiId);

        if (dbEmoji is null)
            return TaskResult.FromFailure("Emoji not found.");

        try
        {
            _db.PlanetEmojis.Remove(dbEmoji);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete planet emoji {EmojiId} on planet {PlanetId}", emojiId, planetId);
            return TaskResult.FromFailure("Failed to delete emoji.");
        }

        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        hosted.RemoveEmoji(emojiId);

        if (notify)
            _coreHub.NotifyPlanetItemDelete(dbEmoji.ToModel());

        return TaskResult.SuccessResult;
    }

    public void NotifyCreated(PlanetEmoji emoji)
    {
        _coreHub.NotifyPlanetItemChange(emoji);
    }

    public async Task<bool> AreAllIdsValidForPlanetAsync(long planetId, IEnumerable<long> ids)
    {
        if (ids is null)
            return true;

        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);

        foreach (var id in ids)
        {
            if (hosted.GetEmoji(id) is null)
                return false;
        }

        return true;
    }
}
