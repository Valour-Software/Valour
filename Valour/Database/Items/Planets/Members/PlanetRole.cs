using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared;
using Valour.Database.Items.Planets.Channels;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets.Members;
using Valour.Shared.Items;
using System.Drawing;
using Valour.Database.Items.Authorization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


namespace Valour.Database.Items.Planets.Members;

public class PlanetRole : Item, ISharedPlanetRole, IPlanetItemAPI<PlanetRole>, INodeSpecific
{
    [InverseProperty("Role")]
    [JsonIgnore]
    public virtual ICollection<PermissionsNode> PermissionNodes { get; set; }

    [ForeignKey("Planet_Id")]
    [JsonIgnore]
    public virtual Planet Planet { get; set; }

    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    public uint Position { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    public ulong Permissions { get; set; }

    // RGB Components for role color
    public byte Color_Red { get; set; }
    public byte Color_Green { get; set; }
    public byte Color_Blue { get; set; }

    // Formatting options
    public bool Bold { get; set; }
    public bool Italics { get; set; }

    public string Name { get; set; }

    public override ItemType ItemType => ItemType.PlanetRole;

    public uint GetAuthority() =>
        ((ISharedPlanetRole)this).GetAuthority();

    public Color GetColor() =>
        ((ISharedPlanetRole)this).GetColor();

    public string GetColorHex() =>
        ((ISharedPlanetRole)this).GetColorHex();

    public bool HasPermission(PlanetPermission perm) =>
        ((ISharedPlanetRole)this).HasPermission(perm);

    public ICollection<PermissionsNode> GetNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.Role_Id == Id).ToList();
        return PermissionNodes;
    }

    public ICollection<PermissionsNode> GetChannelNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.Role_Id == Id).ToList();
        return PermissionNodes.Where(x => x.Target_Type == ItemType.PlanetChatChannel).ToList();
    }

    public ICollection<PermissionsNode> GetCategoryNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.Role_Id == Id).ToList();
        return PermissionNodes.Where(x => x.Target_Type == ItemType.PlanetCategoryChannel).ToList();
    }

    public async Task<PermissionsNode> GetChannelNodeAsync(PlanetChatChannel channel, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == channel.Id &&
                                                                     x.Target_Type == ItemType.PlanetChatChannel);

    public async Task<PermissionsNode> GetChannelNodeAsync(PlanetCategoryChannel category, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == category.Id &&
                                                                     x.Target_Type == ItemType.PlanetChatChannel);

    public async Task<PermissionsNode> GetCategoryNodeAsync(PlanetCategoryChannel category, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == category.Id &&
                                                                     x.Target_Type == ItemType.PlanetCategoryChannel);

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, PlanetChatChannel channel, ValourDB db) =>
        await GetPermissionStateAsync(permission, channel.Id, db);

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, ulong channel_id, ValourDB db) =>
        (await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Role_Id == Id && x.Target_Id == channel_id)).GetPermissionState(permission);

    /// <summary>
    /// Tries to delete this role
    /// </summary>
    public async Task<TaskResult<int>> TryDeleteAsync(PlanetMember member, ValourDB db)
    {
        if (member == null)
            return new TaskResult<int>(false, "Member not found", 404);

        if (member.Planet_Id != Planet_Id)
            return new TaskResult<int>(false, "Member is of another planet", 403);

        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return new TaskResult<int>(false, "Member lacks PlanetPermissions.ManageRoles", 403);

        if (await member.GetAuthorityAsync() <= GetAuthority())
            return new TaskResult<int>(false, "Member authority is lower than role authority", 403);

        Planet ??= await db.Planets.FindAsync(Planet_Id);

        if (Id == Planet.Default_Role_Id)
            return new TaskResult<int>(false, "Cannot remove default role", 400);

        // Remove all members
        var members = db.PlanetRoleMembers.Where(x => x.Role_Id == Id);
        db.PlanetRoleMembers.RemoveRange(members);

        // Remove role nodes
        var nodes = GetNodes(db);

        db.PermissionsNodes.RemoveRange(nodes);

        // Remove self
        db.PlanetRoles.Remove(this);

        await db.SaveChangesAsync();

        // Notify clients
        PlanetHub.NotifyRoleDeletion(this);

        return new TaskResult<int>(true, "Removed role", 200);
    }

    public async Task<TaskResult<int>> TryUpdateAsync(PlanetMember member, PlanetRole newRole, ValourDB db)
    {
        if (member == null)
            return new TaskResult<int>(false, "Member not found", 403);

        if (member.Planet_Id != Planet_Id)
            return new TaskResult<int>(false, "Member is of another planet", 403);

        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return new TaskResult<int>(false, "Member lacks PlanetPermissions.ManageRoles", 403);

        if (await member.GetAuthorityAsync() <= GetAuthority())
            return new TaskResult<int>(false, "Member authority is lower than role authority", 403);

        if (newRole.Id != Id)
            return new TaskResult<int>(false, "Given role does not match id", 400);

        this.Name = newRole.Name;
        this.Position = newRole.Position;
        this.Permissions = newRole.Permissions;
        this.Color_Red = newRole.Color_Red;
        this.Color_Green = newRole.Color_Green;
        this.Color_Blue = newRole.Color_Blue;
        this.Bold = newRole.Bold;
        this.Italics = newRole.Italics;

        db.PlanetRoles.Update(this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyRoleChange(this);

        return new TaskResult<int>(true, "Success", 200);
    }

    public async Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db)
        => await CanCreateAsync(member, db);

    public async Task<TaskResult> CanUpdateAsync(PlanetMember member, ValourDB db)
        => await CanCreateAsync(member, db);

    public async Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db)
    {
        // Needs to be able to GET in order to do anything else
        var canGet = await((IPlanetItemAPI<PlanetRole>)this).CanGetAsync(member, db);
        if (!canGet.Success)
            return canGet;

        if (!await member.HasPermissionAsync(PlanetPermissions.ManageRoles, db))
            return new TaskResult(false, "Member lacks planet permission " + PlanetPermissions.ManageRoles.Name);

        if (GetAuthority() > await member.GetAuthorityAsync())
            return new TaskResult(false, "You cannot manage roles with higher authority than your own.");

        if (Permissions == PlanetPermissions.FullControl.Value
            && (!await member.HasPermissionAsync(PlanetPermissions.FullControl, db)))
            return new TaskResult(false, "Only an admin can manage admin roles.");

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> ValidateItemAsync(PlanetRole old, ValourDB db)
    {
        await ((IPlanetItemAPI<PlanetRole>)this).GetPlanetAsync(db);

        // This role is new
        if (old == null)
        {
            Position = (uint)await db.PlanetRoles.CountAsync(x => x.Planet_Id == Planet_Id);
        }
        else
        {
            if (old.Position != Position)
            {
                return new TaskResult(false, "Position cannot be changed directly.");
            }
        }

        return TaskResult.SuccessResult;
    }

    public async Task DeleteAsync()
    {

    }
}

