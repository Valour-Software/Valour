using IdGen;
using StackExchange.Redis;
using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetChatChannelService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetCategoryService _categoryService;
    private readonly PlanetMemberService _memberService;
    private readonly PermissionsService _permissionsService;
    
    public PlanetChatChannelService(
        ValourDB db, 
        PlanetService planetService,
        PlanetCategoryService categoryService,
        PlanetMemberService memberService,
        PermissionsService permissionsService)
    {
        _db = db;
        _planetService = planetService;
        _categoryService = categoryService;
        _memberService = memberService;
        _permissionsService = permissionsService;
    }

    /// <summary>
    /// Returns the chat channel with the given id
    /// </summary>
    public async ValueTask<PlanetChatChannel> GetAsync(long id) =>
        await _db.PlanetChatChannels.FindAsync(id);

    

    /// <summary>
    /// Soft deletes the given channel
    /// </summary>
    public async Task DeleteAsync(PlanetChatChannel channel)
    {
        channel.IsDeleted = true;
        _db.PlanetChatChannels.Update(channel);
        await _db.SaveChangesAsync();
    }
    
    /// <summary>
    /// The regex used for name validation
    /// </summary>
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");
    
    /// <summary>
    /// Validates that a given name is allowable
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
            return new TaskResult(false, "Channel names must be 32 characters or less.");

        if (!nameRegex.IsMatch(name))
            return new TaskResult(false, "Channel names may only include letters, numbers, dashes, and underscores.");

        return new TaskResult(true, "The given name is valid.");
    }

    /// <summary>
    /// Validates that a given description is allowable
    /// </summary>
    public static TaskResult ValidateDescription(string desc)
    {
        if (desc.Length > 500)
        {
            return new TaskResult(false, "Planet descriptions must be 500 characters or less.");
        }

        return TaskResult.SuccessResult;
    }

    public static async Task<TaskResult> ValidateParentAndPosition(ValourDB db, PlanetChatChannel channel)
    {
        // Logic to check if parent is legitimate
        if (channel.ParentId is not null)
        {
            var parent = await db.PlanetCategoryChannels.FirstOrDefaultAsync
            (x => x.Id == channel.ParentId
                  && x.PlanetId == channel.PlanetId); // This ensures the result has the same planet id

            if (parent is null)
                return new TaskResult(false, "Parent ID is not valid");
        }

        // Auto determine position
        if (channel.Position < 0)
        {
            channel.Position = (ushort)(await db.PlanetChannels.CountAsync(x => x.ParentId == channel.ParentId));
        }
        else
        {
            if (!await HasUniquePosition(db, channel))
                return new TaskResult(false, "The position is already taken.");
        }

        return new TaskResult(true, "Valid");
    }
}