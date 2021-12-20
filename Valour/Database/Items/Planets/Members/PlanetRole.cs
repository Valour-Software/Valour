using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared;
using Valour.Database.Items.Planets.Channels;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets.Members;
using Valour.Shared.Items;
using System.Drawing;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


namespace Valour.Database.Items.Planets.Members;

public class PlanetRole : NamedItem, ISharedPlanetRole
{
    [ForeignKey("Planet_Id")]
    [JsonIgnore]
    public virtual Planet Planet { get; set; }

    [InverseProperty("Role")]
    [JsonIgnore]
    public virtual ICollection<Authorization.PermissionsNode> PermissionNodes { get; set; }

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    [JsonPropertyName("Position")]
    public uint Position { get; set; }

    /// <summary>
    /// The ID of the planet or system this role belongs to
    /// </summary>
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    [JsonPropertyName("Permissions")]
    public ulong Permissions { get; set; }

    // RGB Components for role color
    [JsonPropertyName("Color_Red")]
    public byte Color_Red { get; set; }

    [JsonPropertyName("Color_Green")]
    public byte Color_Green { get; set; }

    [JsonPropertyName("Color_Blue")]
    public byte Color_Blue { get; set; }

    // Formatting options
    [JsonPropertyName("Bold")]
    public bool Bold { get; set; }

    [JsonPropertyName("Italics")]
    public bool Italics { get; set; }

    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.PlanetRole;

    public uint GetAuthority() =>
        ((ISharedPlanetRole)this).GetAuthority();


    public Color GetColor() =>
        ((ISharedPlanetRole)this).GetColor();


    public string GetColorHex() =>
        ((ISharedPlanetRole)this).GetColorHex();


    public bool HasPermission(PlanetPermission perm) =>
        ((ISharedPlanetRole)this).HasPermission(perm);

    public ICollection<Authorization.PermissionsNode> GetNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.Role_Id == Id).ToList();
        return PermissionNodes;
    }

    public ICollection<Authorization.PermissionsNode> GetChannelNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.Role_Id == Id).ToList();
        return PermissionNodes.Where(x => x.Target_Type == Shared.Items.ItemType.ChatChannel).ToList();
    }

    public ICollection<Authorization.PermissionsNode> GetCategoryNodes(ValourDB db)
    {
        PermissionNodes ??= db.PermissionsNodes.Where(x => x.Role_Id == Id).ToList();
        return PermissionNodes.Where(x => x.Target_Type == Shared.Items.ItemType.Category).ToList();
    }

    public async Task<Authorization.PermissionsNode> GetChannelNodeAsync(PlanetChatChannel channel, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == channel.Id &&
                                                                     x.Target_Type == Shared.Items.ItemType.ChatChannel);

    public async Task<Authorization.PermissionsNode> GetChannelNodeAsync(PlanetCategory category, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == category.Id &&
                                                                     x.Target_Type == Shared.Items.ItemType.ChatChannel);

    public async Task<Authorization.PermissionsNode> GetCategoryNodeAsync(PlanetCategory category, ValourDB db) =>
        await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Target_Id == category.Id &&
                                                                     x.Target_Type == Shared.Items.ItemType.Category);

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, PlanetChatChannel channel, ValourDB db) =>
        await GetPermissionStateAsync(permission, channel.Id, db);

    public async Task<PermissionState> GetPermissionStateAsync(Permission permission, ulong channel_id, ValourDB db) =>
        (await db.PermissionsNodes.FirstOrDefaultAsync(x => x.Role_Id == Id && x.Target_Id == channel_id)).GetPermissionState(permission);

    /// <summary>
    /// Returns if the role has the permission
    /// </summary>
    /// <param name="permission"></param>
    /// <returns></returns>
    public bool HasPermission(PlanetPermission permission)
    {
        return Permission.HasPermission(Permissions, permission);
    }

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
}

