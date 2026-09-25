using StackExchange.Redis;
using Valour.Database;
using Valour.Shared;
using PlanetTag = Valour.Server.Models.PlanetTag;

namespace Valour.Server.Services;


public class TagService : ITagService
{
    /// <summary>
    /// Most tags returned by one list request. Curated tags come first so the
    /// onboarding picker always receives them.
    /// </summary>
    public const int MaxTagListSize = 2500;

    /// <summary>
    /// Tags are global, so the number that users can create is bounded both
    /// overall and per user. The overall cap stays below the list size so
    /// clients that filter the full list locally still see every tag.
    /// </summary>
    public const int MaxUserCreatedTags = 2000;

    public const int MaxTagsPerUserPerDay = 10;

    private static readonly TimeSpan CreationWindow = TimeSpan.FromDays(1);

    private readonly ValourDb _db;
    private readonly ILogger<TagService> _logger;
    private readonly IConnectionMultiplexer _redis;

    public TagService(ValourDb db, ILogger<TagService> logger, IConnectionMultiplexer redis)
    {
        _db = db;
        _logger = logger;
        _redis = redis;
    }


    public async Task<List<PlanetTag>> GetAllTagsList(int skip = 0, int take = MaxTagListSize)
    {
        if (skip < 0)
            skip = 0;

        if (take < 1 || take > MaxTagListSize)
            take = MaxTagListSize;

        var tags = await _db.Tags
            .AsNoTracking()
            .OrderByDescending(x => x.Curated)
            .ThenBy(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var tagsDtoList = tags.Select(tag => PlanetTagMapper.ToModel(tag)).ToList();
        return tagsDtoList;
    }

    public async Task<TaskResult<PlanetTag>> CreateAsync(PlanetTag model, long userId)
    {
        var baseValidation= ValidateTag(model);

        if(!baseValidation.Success)
            return new TaskResult<PlanetTag>(false, baseValidation.Message);

        var slug = model.Slug.ToLower();
        if (await _db.Tags.AnyAsync(x => x.Slug.ToLower() == slug))
            return new TaskResult<PlanetTag>(false, "A tag with this slug already exists.");

        if (await _db.Tags.CountAsync(x => !x.Curated) >= MaxUserCreatedTags)
            return new TaskResult<PlanetTag>(false, "New tags cannot be created right now. Please use an existing tag.");

        if (!await TryConsumeCreationAsync(userId))
            return new TaskResult<PlanetTag>(false,
                $"You can create up to {MaxTagsPerUserPerDay} tags per day. Please try again later.");

        var tag = model.ToDatabase();
        tag.Created = DateTime.UtcNow;
        // Belt and braces: user-created tags are never curated
        tag.Curated = false;
        try 
        {
            await _db.Tags.AddAsync(tag);
            await _db.SaveChangesAsync();
            
        }catch (Exception e)
        {
            _logger.LogError(e, "Failed to create tag");
            return new TaskResult<PlanetTag>(false, "Failed to create tag");
        }
        
        var returnModel = tag.ToModel();
        return new TaskResult<PlanetTag>(true, "Tag created successfully", returnModel);
    }

    public async Task<TaskResult<PlanetTag>> FindAsync(long tagId)
    {
        var dbTag = await _db.Tags.FindAsync(tagId);
        var tag = dbTag.ToModel();
        
        if(dbTag != null)
            return new TaskResult<PlanetTag>(true, "Tag found",tag);
        
        return new TaskResult<PlanetTag>(false, "Tag not found");
    }

    /// <summary>
    /// Counts a tag creation against the user's daily allowance. The counter is
    /// kept in Redis so every server replica shares it.
    /// </summary>
    private async Task<bool> TryConsumeCreationAsync(long userId)
    {
        var db = _redis.GetDatabase();
        var key = $"tags:created:{userId}";

        var count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, CreationWindow);

        return count <= MaxTagsPerUserPerDay;
    }

    private TaskResult ValidateTag(PlanetTag planetTag)
    {
        if (planetTag is null)
            return new TaskResult(false, "The tag cannot be null.");

        // Validate Name
        var nameValid = ValidateName(planetTag.Name);
        if (!nameValid.Success)
            return new TaskResult(false, nameValid.Message);

        // Validate Slug
        var slugValid = ValidateSlug(planetTag.Slug);
        if (!slugValid.Success)
            return new TaskResult(false, slugValid.Message);
        
        
        return TaskResult.SuccessResult;
    }
    

    private TaskResult ValidateSlug(string tagSlug)
    {
        if (string.IsNullOrWhiteSpace(tagSlug))
        {
            return new TaskResult(false, "Tag slug cannot be empty.");
        }

        if (tagSlug.Length > 16)
        {
            return new TaskResult(false, "Tag slug must be 16 characters or less.");
        }
        
        return new TaskResult(true,"The given slug is valid.");
    }

    private TaskResult ValidateName(string tagName)
    {
        {
            if (string.IsNullOrEmpty(tagName))
            {
                return new TaskResult(false, "Tag name cannot be empty.");
            }

            if (tagName.Length > 32)
            {
                return new TaskResult(false, "Tag name must be 32 characters or less.");
            }
        
            return new TaskResult(true,"The given name is valid.");
        }
    }
}

public interface ITagService
{
    public  Task<List<PlanetTag>> GetAllTagsList(int skip = 0, int take = TagService.MaxTagListSize);
    public Task<TaskResult<PlanetTag>> CreateAsync(PlanetTag planetTag, long userId);
    Task<TaskResult<PlanetTag>> FindAsync(long tagId);
}