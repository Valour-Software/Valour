using IdGen;
using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Shared;

namespace Valour.Server.Services;

public class PlanetService
{
    private readonly ValourDB _db;
    private readonly PlanetRoleService _planetRoleService;
    private readonly PlanetMemberService _planetMemberService;
    private readonly PlanetChatChannelService _chatChannelService;
    private readonly PlanetCategoryService _categoryService;
    private readonly CoreHubService _coreHub;
    
    public PlanetService(
        ValourDB db, 
        PlanetRoleService roleService, 
        PlanetMemberService memberService,
        PlanetChatChannelService chatChannelService,
        PlanetCategoryService categoryService,
        CoreHubService coreHub)
    {
        _db = db;
        _planetRoleService = roleService;
        _planetMemberService = memberService;
        _chatChannelService = chatChannelService;
        _categoryService = categoryService;
        _coreHub = coreHub;
    }

    /// <summary>
    /// Returns the planet with the given id
    /// </summary>
    public ValueTask<Planet> GetAsync(long id) =>
        _db.Planets.FindAsync(id);

    /// <summary>
    /// Returns the primary channel for the given planet
    /// </summary>
    public async ValueTask<PlanetChatChannel> GetPrimaryChannelAsync(Planet planet)
    {
        planet.PrimaryChannel ??= await _chatChannelService.GetAsync(planet.PrimaryChannelId);
        return planet.PrimaryChannel;
    }

    /// <summary>
    /// Returns the default role for the given planet
    /// </summary>
    public async ValueTask<PlanetRole> GetDefaultRole(Planet planet)
    {
        planet.DefaultRole ??= await _planetRoleService.GetAsync(planet.DefaultRoleId);
        return planet.DefaultRole;
    }

    /// <summary>
    /// Returns the roles for the given planet
    /// </summary>
    public async ValueTask<ICollection<PlanetRole>> GetRolesAsync(Planet planet)
    {
        planet.Roles ??= await _db.PlanetRoles.Where(x => x.PlanetId == planet.Id).ToListAsync();
        return planet.Roles;
    }

    /// <summary>
    /// Adds the given user to the given planet as a member
    /// </summary>
    public async Task<TaskResult<PlanetMember>> AddMemberAsync(Planet planet, User user, bool doTransaction)
    {
        if (await _db.PlanetBans.AnyAsync(x => x.TargetId == user.Id && x.PlanetId == planet.Id &&
            (x.TimeExpires != null && x.TimeExpires > DateTime.UtcNow)))
        {
            return new TaskResult<PlanetMember>(false, "You are banned from this planet.");
        }

        // New member
        PlanetMember member;
        
        // See if there is an old member
        var oldMember = await _planetMemberService.GetIncludingDeletedByUserAsync(user.Id, planet.Id);
        
        // If there is an old member, we can just restore it
        bool rejoin = false;

        // Already a member
        if (oldMember is not null)
        {
            // If the member already exists and is not deleted, do nothing
            if (!oldMember.IsDeleted)
            {
                return new TaskResult<PlanetMember>(false, "Already a member.", null);
            }
            // Set old member to be restored
            else
            {
                member = oldMember;
                rejoin = true;
            }
        }
        else 
        {
            member = new PlanetMember()
            {
                Id = IdManager.Generate(),
                Nickname = user.Name,
                PlanetId = planet.Id,
                UserId = user.Id
            };
        }

        // Add to default planet role
        var roleMember = new PlanetRoleMember()
        {
            Id = IdManager.Generate(),
            PlanetId = planet.Id,
            UserId = user.Id,
            RoleId = planet.DefaultRoleId,
            MemberId = member.Id
        };
        
        IDbContextTransaction trans = null;

        if (doTransaction)
            trans = await _db.Database.BeginTransactionAsync();

        try
        {
            if (rejoin)
            {
                member.IsDeleted = false;
                _db.PlanetMembers.Update(member);
            }
            else
            {
                await _db.PlanetMembers.AddAsync(member);
            }
            
            await _db.PlanetRoleMembers.AddAsync(roleMember);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            await trans.RollbackAsync();
            return new TaskResult<PlanetMember>(false, e.Message);
        }

        if (doTransaction)
            await trans.CommitAsync();

        _coreHub.NotifyPlanetItemChange(member);

        Console.WriteLine($"User {user.Name} ({user.Id}) has joined {planet.Name} ({planet.Id})");

        return new TaskResult<PlanetMember>(true, "Success", member);
    }

    /// <summary>
    /// Soft deletes the given planet
    /// </summary>
    public async Task DeleteAsync(Planet planet)
    {
        planet.IsDeleted = true;
        _db.Planets.Update(planet);
        await _db.SaveChangesAsync();
    }
}