using Valour.Database;
using Valour.Shared;
using PlanetTag = Valour.Server.Models.PlanetTag;

namespace Valour.Server.Services;


public class TagService : ITagService
{
    private readonly ValourDb _db;
    private readonly ILogger<WalletService> _logger;

    public TagService(ValourDb db, ILogger<WalletService> logger)
    {
        _db = db;
        _logger = logger;
    }


    public async Task<List<PlanetTag>> GetAllTagsList()
    {
        var tags = await _db.Tags.ToListAsync();
        var tagsDtoList = tags.Select(tag => PlanetTagMapper.ToModel(tag)).ToList();
        return tagsDtoList;
    }

    public async Task<TaskResult<PlanetTag>> CreateAsync(PlanetTag model)
    {
        var baseValidation= ValidateTag(model);
        
        if(!baseValidation.Success)
            return new TaskResult<PlanetTag>(false, baseValidation.Message);
        
        var tag = model.ToDatabase();
        tag.Created = DateTime.UtcNow;
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

    private TaskResult ValidateTag(PlanetTag planetTag)
    {
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
    public  Task<List<PlanetTag>> GetAllTagsList();
    public Task<TaskResult<PlanetTag>> CreateAsync(PlanetTag planetTag);
    Task<TaskResult<PlanetTag>> FindAsync(long tagId);
}