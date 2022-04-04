/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Authorization;

/// <summary>
/// Permissions are basic flags used to denote if actions are allowed
/// to be taken on one's behalf
/// </summary>
public class Permission
{
    /// <summary>
    /// Permission node to have complete control
    /// </summary>
    public const ulong FULL_CONTROL = ulong.MaxValue;

    /// <summary>
    /// The name of this permission
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The description of this permission
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// The value of this permission
    /// </summary>
    public ulong Value { get; set; }

    /// <summary>
    /// Constant used for mixed permission name
    /// </summary>
    const string Mixed_Name = "Mixed permissions";

    /// <summary>
    /// Constant used for mixed permission description
    /// </summary>
    const string Mixed_Description = "A mix of several permissions";

    /// <summary>
    /// Initializes the permission
    /// </summary>
    public Permission(ulong value, string name, string description)
    {
        this.Name = name;
        this.Description = description;
        this.Value = value;
    }

    /// <summary>
    /// Returns whether the given code includes the given permission
    /// </summary>
    public static bool HasPermission(ulong code, Permission permission)
    {
        // Case if full control is granted
        if (code == FULL_CONTROL) return true;

        // Otherwise check for specific permission
        return (code & permission.Value) == permission.Value;
    }

    /// <summary>
    /// Creates and returns a permission code from given permissions 
    /// </summary>
    public static ulong CreateCode(params Permission[] permissions)
    {
        ulong code = 0x00;

        foreach (Permission permission in permissions)
        {
            if (permission != null)
            {
                code |= permission.Value;
            }
        }

        return code;
    }

    /// <summary>
    /// Creates a hybrid permission from the given permissions
    /// </summary>
    public static Permission Create(params Permission[] permissions)
    {
        var code = CreateCode(permissions);
        return new Permission(code, Mixed_Name, Mixed_Description);
    }

    public static Permission operator +(Permission a, Permission b)
    {
        return new Permission(a.Value | b.Value, Mixed_Name, Mixed_Description);
    }

    public static Permission operator -(Permission a, Permission b)
    {
        return new Permission(a.Value & (~b.Value), Mixed_Name, Mixed_Description);
    }
}

public class ChatChannelPermission : Permission
{
    public ChatChannelPermission(ulong value, string name, string description) : base(value, name, description)
    {
    }
}

public class CategoryPermission : Permission
{
    public CategoryPermission(ulong value, string name, string description) : base(value, name, description)
    {
    }
}

public class UserPermission : Permission
{
    public UserPermission(ulong value, string name, string description) : base(value, name, description)
    {
    }
}

public class PlanetPermission : Permission
{
    public PlanetPermission(ulong value, string name, string description) : base(value, name, description)
    {
    }
}

/// <summary>
/// This class contains all user permissions and helper methods for working
/// with them.
/// </summary>
public static class UserPermissions
{
    /// <summary>
    /// Contains every user permission
    /// </summary>
    public static UserPermission[] Permissions;

    static UserPermissions()
    {
        Permissions = new UserPermission[]
        {
                FullControl,
                Minimum,
                View,
                Membership,
                Invites
        };
    }

    // Use shared full control definition
    public static readonly UserPermission FullControl = new UserPermission(Permission.FULL_CONTROL, "Full Control", "Control every part of your account.");

    // Every subsequent permission has double the value (the next bit)
    // An update should NEVER change the order or value of old permissions
    // As that would be a massive security issue
    public static readonly UserPermission Minimum = new UserPermission(0x01, "Minimum", "Allows this app to only view your account ID when authorized.");
    public static readonly UserPermission View = new UserPermission(0x02, "View", "Allows this app to access basic information about your account.");
    public static readonly UserPermission Membership = new UserPermission(0x04, "Membership", "Allows this app to view the planets you are a member of.");
    public static readonly UserPermission Invites = new UserPermission(0x08, "Invites", "Allows this app to view the planets you are invited to.");
    public static readonly UserPermission PlanetManagement = new UserPermission(0x10, "Planet Management", "Allows this app to manage your planets.");
}

/// <summary>
/// This class contains all channel permissions and helper methods for working
/// with them
/// </summary>
public static class ChatChannelPermissions
{

    public static readonly ulong Default;

    /// <summary>
    /// Contains every channel permission
    /// </summary>
    public static ChatChannelPermission[] Permissions;


    // Use shared full control definition
    public static readonly ChatChannelPermission FullControl;

    public static readonly ChatChannelPermission View;
    public static readonly ChatChannelPermission ViewMessages;
    public static readonly ChatChannelPermission PostMessages;
    public static readonly ChatChannelPermission ManageChannel;
    public static readonly ChatChannelPermission ManagePermissions;
    public static readonly ChatChannelPermission Embed;
    public static readonly ChatChannelPermission AttachContent;
    public static readonly ChatChannelPermission ManageMessages;

    static ChatChannelPermissions()
    {
        FullControl = new ChatChannelPermission(Permission.FULL_CONTROL, "Full Control", "Allow members full control of the channel");
        View = new ChatChannelPermission(0x01, "View", "Allow members to view this channel in the channel list.");
        ViewMessages = new ChatChannelPermission(0x02, "View Messages", "Allow members to view the messages within this channel.");
        PostMessages = new ChatChannelPermission(0x04, "Post", "Allow members to post messages to this channel.");
        ManageChannel = new ChatChannelPermission(0x08, "Manage", "Allow members to manage this channel's details.");
        ManagePermissions = new ChatChannelPermission(0x10, "Permissions", "Allow members to manage permissions for this channel.");
        Embed = new ChatChannelPermission(0x20, "Embed", "Allow members to post embedded content to this channel.");
        AttachContent = new ChatChannelPermission(0x40, "Attach Content", "Allow members to upload files to this channel.");
        ManageMessages = new ChatChannelPermission(0x80, "Manage Messages", "Allow members to delete and manage messages in this channel.");

        Permissions = new ChatChannelPermission[]
        {
                FullControl,
                View,
                ViewMessages,
                PostMessages,
                ManageChannel,
                ManagePermissions,
                Embed,
                AttachContent,
                ManageMessages
        };

        Default = Permission.CreateCode(View, ViewMessages, PostMessages);
    }
}

/// <summary>
/// This class contains all category permissions and helper methods for working
/// with them
/// </summary>
public static class CategoryPermissions
{

    public static readonly ulong Default;

    /// <summary>
    /// Contains every category permission
    /// </summary>
    public static CategoryPermission[] Permissions;


    // Use shared full control definition
    public static readonly CategoryPermission FullControl;

    public static readonly CategoryPermission View;
    public static readonly CategoryPermission ManageCategory;
    public static readonly CategoryPermission ManagePermissions;

    static CategoryPermissions()
    {
        FullControl = new CategoryPermission(Permission.FULL_CONTROL, "Full Control", "Allow members full control of the channel");
        View = new CategoryPermission(0x01, "View", "Allow members to view this channel in the channel list.");
        ManageCategory = new CategoryPermission(0x08, "Manage", "Allow members to manage this channel's details.");
        ManagePermissions = new CategoryPermission(0x10, "Permissions", "Allow members to manage permissions for this channel.");

        Permissions = new CategoryPermission[]
        {
                FullControl,
                View,
                ManageCategory,
                ManagePermissions,
        };

        Default = Permission.CreateCode(View);
    }
}

/// <summary>
/// This class contains all planet permissions and helper methods for working
/// with them
/// </summary>
public static class PlanetPermissions
{
    public static readonly ulong Default =
        Permission.CreateCode(View);

    /// <summary>
    /// Contains every planet permission
    /// </summary>
    public static PlanetPermission[] Permissions;

    static PlanetPermissions()
    {
        Permissions = new PlanetPermission[]
        {
                FullControl,
                View,
                Invite,
                DisplayRole,
                Manage,
                Kick,
                Ban,
                ManageCategories,
                ManageChannels,
                ManageRoles
        };
    }

    // Use shared full control definition
    public static readonly PlanetPermission FullControl = new PlanetPermission(Permission.FULL_CONTROL, "Full Control", "Allow members full control of the planet (owner)");

    public static readonly PlanetPermission View = new PlanetPermission(0x01, "View", "Allow members to view the planet. This is implicitly granted to members."); // Implicitly granted to members
    public static readonly PlanetPermission Invite = new PlanetPermission(0x02, "Invite", "Allow members to send invites to the planet.");
    public static readonly PlanetPermission DisplayRole = new PlanetPermission(0x04, "Display Role", "Enables a role to be displayed seperately in the role list.");
    public static readonly PlanetPermission Manage = new PlanetPermission(0x08, "Manage Planet", "Allow members to modify base planet settings.");
    public static readonly PlanetPermission Kick = new PlanetPermission(0x10, "Kick Members", "Allow members to kick other members.");
    public static readonly PlanetPermission Ban = new PlanetPermission(0x20, "Ban Members", "Allow members to ban other members.");
    public static readonly PlanetPermission ManageCategories = new PlanetPermission(0x40, "Manage Categories", "Allow members to manage categories.");
    public static readonly PlanetPermission ManageChannels = new PlanetPermission(0x80, "Manage Channels", "Allow members to manage channels.");
    public static readonly PlanetPermission ManageRoles = new PlanetPermission(0x100, "Manage Roles", "Allow members to manage roles.");

}

public enum PermissionState
{
    Undefined, True, False
}

/// <summary>
/// Permission codes use two ulongs to represent
/// three possible states for every permission
/// </summary>
public struct PermissionNodeCode
{
    // Just for reference,
    // If the mask bit is 0, then it is always undefined
    // If the mask but is 1, then if the code bit is 1 it is true. Otherwise it is false.
    // This basically compresses 64 booleans (64 bytes) into 2 ulongs (16 bytes)

    public ulong Code { get; set; }
    public ulong Mask { get; set; }

    public PermissionNodeCode(ulong code, ulong mask)
    {
        this.Code = code;
        this.Mask = mask;
    }

    public PermissionState GetState(Permission permission)
    {
        // If the lowest, or view, permission is false, all other permissions are automatically false
        if ((Mask & 0x01) == 0 || (Code & 0x01) == 0)
        {
            return PermissionState.False;
        }

        if ((Mask & permission.Value) != permission.Value)
        {
            return PermissionState.Undefined;
        }

        if ((Code & permission.Value) != permission.Value)
        {
            return PermissionState.False;
        }

        return PermissionState.True;
    }
}

