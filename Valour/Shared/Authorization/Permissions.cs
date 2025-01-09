/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

using Valour.Shared.Models;

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
    public const long FULL_CONTROL = ~(0x0);

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
    public long Value { get; set; }

    /// <summary>
    /// Constant used for mixed permission name
    /// </summary>
    const string Mixed_Name = "Mixed permissions";

    /// <summary>
    /// Constant used for mixed permission description
    /// </summary>
    const string Mixed_Description = "A mix of several permissions";

    public virtual string ReadableName => "Base";
    
    public virtual ChannelTypeEnum TargetType => ChannelTypeEnum.Undefined;

    public virtual long GetDefault() => 0;

    /// <summary>
    /// Initializes the permission
    /// </summary>
    public Permission(long value, string name, string description)
    {
        this.Name = name;
        this.Description = description;
        this.Value = value;
    }

    /// <summary>
    /// Returns whether the given code includes the given permission
    /// </summary>
    public static bool HasPermission(long code, Permission permission)
    {
        // Case if full control is granted
        if (code == FULL_CONTROL) return true;

        // Otherwise check for specific permission
        return (code & permission.Value) == permission.Value;
    }

    /// <summary>
    /// Creates and returns a permission code from given permissions 
    /// </summary>
    public static long CreateCode(params Permission[] permissions)
    {
        long code = 0x00;

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

public class ChannelPermissionGroup
{
    public ChannelTypeEnum TargetType { get; set; }
    public Permission[] Permissions { get; set; }
}

public static class ChannelPermissions
{
    public static readonly ChannelPermissionGroup[] ChannelTypes = new ChannelPermissionGroup[3];

    public const long ViewValue = 0x01;
    // There is a gap here in values! 0x01 -> 0x08
    public const long ManageValue = 0x08;
    public const long PermissionsValue = 0x10;
    
    public static readonly ChannelPermission View;
    public static readonly ChannelPermission Manage;
    public static readonly ChannelPermission ManagePermissions;
    
    static ChannelPermissions() 
    {
        View = new CategoryPermission(ChannelPermissions.ViewValue, "View", "Allow members to view the channel/category in the channel list.");
        Manage = new CategoryPermission(ChannelPermissions.ManageValue, "Manage", "Allow members to manage the channel/category's details.");
        ManagePermissions = new CategoryPermission(ChannelPermissions.PermissionsValue, "Permissions", "Allow members to manage permissions for the channel/category.");
    }
    
    public static Permission[] GetChannelPermissionSet(ChannelTypeEnum type)
    {
        switch (type)
        {
            case ChannelTypeEnum.PlanetChat:
                return ChatChannelPermissions.Permissions;
            case ChannelTypeEnum.PlanetCategory:
                return CategoryPermissions.Permissions;
            case ChannelTypeEnum.PlanetVoice:
                return VoiceChannelPermissions.Permissions;
            default:
                throw new Exception($"Invalid channel type {type}");
        }
    }
}

public abstract class ChannelPermission : Permission
{
    public override ChannelTypeEnum TargetType => ChannelTypeEnum.Undefined;
    public ChannelPermission(long value, string name, string description) : base(value, name, description)
    {
    }
}

public class ChatChannelPermission : ChannelPermission
{
    public override ChannelTypeEnum TargetType => ChannelTypeEnum.PlanetChat;
    public override string ReadableName => "Chat Channel";
    public override long GetDefault() => ChatChannelPermissions.Default;
    public ChatChannelPermission(long value, string name, string description) : base(value, name, description)
    {
    }
}

public class CategoryPermission : ChannelPermission
{
    public override ChannelTypeEnum TargetType => ChannelTypeEnum.PlanetCategory;
    public override string ReadableName => "Category";
    public override long GetDefault() => CategoryPermissions.Default;
    public CategoryPermission(long value, string name, string description) : base(value, name, description)
    {
    }
}

public class VoiceChannelPermission : ChannelPermission
{
    public override ChannelTypeEnum TargetType => ChannelTypeEnum.PlanetVoice;
    public override string ReadableName => "Voice Channel";
    public override long GetDefault() => VoiceChannelPermissions.Default;
    public VoiceChannelPermission(long value, string name, string description) : base(value, name, description)
    {
    }
}

public class UserPermission : Permission
{
    public override string ReadableName => "User";
    public UserPermission(long value, string name, string description) : base(value, name, description)
    {
    }
}

public class PlanetPermission : Permission
{
    public override string ReadableName => "Planet";
    public PlanetPermission(long value, string name, string description) : base(value, name, description)
    {
    }
}

public enum UserPermissionsEnum
{
    FullControl, 
    Minimum,
    View,
    Membership,
    Invites,
    PlanetManagement,
    Messages,
    Friends,
    DirectMessages,

    // A whole lot of eco permissions.
    // We want fine-grained control for oauth
    EconomyPlanetView,
    EconomyPlanetSend,
    EconomyViewGlobal,
    EconomySendGlobal,
    EconomyPlanetTrade,
    EconomyGlobalTrade
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
                Invites,
                PlanetManagement,
                Messages,
                Friends,
                DirectMessages,
                EconomyViewPlanet,
                EconomySendPlanet,
                EconomyViewGlobal,
                EconomySendGlobal,
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
    public static readonly UserPermission Messages = new UserPermission(0x20, "Messages", "Allows this app to send and receive messages.");
    public static readonly UserPermission Friends = new UserPermission(0x40, "Friends", "Allows this app to view and manage your friends.");
    public static readonly UserPermission DirectMessages = new UserPermission(0x80, "Direct Messages", "Allows this app to view your direct messages.");

    // Economy permissions
    public static readonly UserPermission EconomyViewPlanet = new UserPermission(0x100, "Economy (Planets) - View", "Allows this app to view your planet eco accounts.");
    public static readonly UserPermission EconomySendPlanet = new UserPermission(0x200, "Economy (Planets) - Send", "Allows this app to send money from your planet eco accounts.");
    public static readonly UserPermission EconomyViewGlobal = new UserPermission(0x400, "Economy (Global) - View", "Allows this app to view your global (Valour Credits) eco account.");
    public static readonly UserPermission EconomySendGlobal = new UserPermission(0x800, "Economy (Global) - Send", "Allows this app to send money from your global (Valour Credits) eco account.");
}

public enum ChatChannelPermissionsEnum
{
    FullControl,
    View,
    ViewMessages,
    PostMessages,
    ManageChannel,
    ManagePermissions,
    Embed,
    AttachContent,
    ManageMessages,
    UseEconomy,
}

/// <summary>
/// This class contains all channel permissions and helper methods for working
/// with them
/// </summary>
public static class ChatChannelPermissions
{

    public static readonly long Default;

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


    // Eco permissions
    public static readonly ChatChannelPermission UseEconomy;

    static ChatChannelPermissions()
    {
        FullControl = new ChatChannelPermission(Permission.FULL_CONTROL, "Full Control", "Allow members full control of the channel");
        View = new ChatChannelPermission(ChannelPermissions.ViewValue, "View", "Allow members to view the channel in the channel list.");
        ViewMessages = new ChatChannelPermission(0x02, "View Messages", "Allow members to view the messages within the channel.");
        PostMessages = new ChatChannelPermission(0x04, "Post", "Allow members to post messages to the channel.");
        ManageChannel = new ChatChannelPermission(ChannelPermissions.ManageValue, "Manage", "Allow members to manage the channel's details.");
        ManagePermissions = new ChatChannelPermission(ChannelPermissions.PermissionsValue, "Permissions", "Allow members to manage permissions for the channel.");
        Embed = new ChatChannelPermission(0x20, "Embed", "Allow members to post embedded content to the channel.");
        AttachContent = new ChatChannelPermission(0x40, "Attach Content", "Allow members to upload files to the channel.");
        ManageMessages = new ChatChannelPermission(0x80, "Manage Messages", "Allow members to delete and manage messages in the channel.");
        UseEconomy = new ChatChannelPermission(0x100, "Use Economy", "Allow members to use economic features in this channel.");

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
                ManageMessages,
                UseEconomy,
        };

        Default = Permission.CreateCode(View, ViewMessages, PostMessages);

        ChannelPermissions.ChannelTypes[0] = new ChannelPermissionGroup()
        {
            TargetType = ChannelTypeEnum.PlanetChat,
            Permissions = ChatChannelPermissions.Permissions
        };
    }
}

public enum CategoryPermissionsEnum
{
    FullControl,
    View,
    ManageCategory,
    ManagePermissions,
}

/// <summary>
/// This class contains all category permissions and helper methods for working
/// with them
/// </summary>
public static class CategoryPermissions
{

    public static readonly long Default;

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
        FullControl = new CategoryPermission(Permission.FULL_CONTROL, "Full Control", "Allow members full control of the category");
        View = new CategoryPermission(ChannelPermissions.ViewValue, "View", "Allow members to view the category in the channel list.");
        ManageCategory = new CategoryPermission(ChannelPermissions.ManageValue, "Manage", "Allow members to manage the category's details.");
        ManagePermissions = new CategoryPermission(ChannelPermissions.PermissionsValue, "Permissions", "Allow members to manage permissions for the category.");

        Permissions = new CategoryPermission[]
        {
                FullControl,
                View,
                ManageCategory,
                ManagePermissions,
        };

        Default = Permission.CreateCode(View);

        ChannelPermissions.ChannelTypes[1] = new ChannelPermissionGroup()
        {
            TargetType = ChannelTypeEnum.PlanetCategory,
            Permissions = CategoryPermissions.Permissions
        };
    }
}

public enum VoiceChannelPermissionsEnum
{
    FullControl,
    View,
    Join,
    Speak,
    ManageChannel,
    ManagePermissions,
}

/// <summary>
/// This class contains all voice channel permissions and helper methods for working
/// with them
/// </summary>
public static class VoiceChannelPermissions
{

    public static readonly long Default;

    /// <summary>
    /// Contains every voice channel permission
    /// </summary>
    public static VoiceChannelPermission[] Permissions;


    // Use shared full control definition
    public static readonly VoiceChannelPermission FullControl;

    public static readonly VoiceChannelPermission View;
    public static readonly VoiceChannelPermission Join;
    public static readonly VoiceChannelPermission Speak;
    public static readonly VoiceChannelPermission ManageChannel;
    public static readonly VoiceChannelPermission ManagePermissions;

    static VoiceChannelPermissions()
    {
        FullControl = new VoiceChannelPermission(Permission.FULL_CONTROL, "Full Control", "Allow members full control of the channel");
        View = new VoiceChannelPermission(ChannelPermissions.ViewValue, "View", "Allow members to view the channel in the channel list.");
        Join = new VoiceChannelPermission(0x02, "Join Channel", "Allow members to connect to the voice channel.");
        Speak = new VoiceChannelPermission(0x04, "Speak", "Allow members to speak in the channel.");
        ManageChannel = new VoiceChannelPermission(ChannelPermissions.ManageValue, "Manage", "Allow members to manage the channel's details.");
        ManagePermissions = new VoiceChannelPermission(ChannelPermissions.PermissionsValue, "Permissions", "Allow members to manage permissions for the channel.");

        Permissions = new VoiceChannelPermission[]
        {
                FullControl,
                View,
                Join,
                Speak,
                ManageChannel,
                ManagePermissions
        };

        Default = Permission.CreateCode(View, Join, Speak);

        ChannelPermissions.ChannelTypes[2] = new ChannelPermissionGroup()
        {
            TargetType = ChannelTypeEnum.PlanetVoice,
            Permissions = VoiceChannelPermissions.Permissions
        };
    }
}

public enum PlanetPermissionsEnum
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
    ManageRoles,

    // Eco Permissions
    UseEconomy,
    ManageCurrency,
    ManageEcoAccounts,
    ForceTransactions,
}

/// <summary>
/// This class contains all planet permissions and helper methods for working
/// with them
/// </summary>
public static class PlanetPermissions
{
    public static readonly long Default =
        Permission.CreateCode(View, UseEconomy);

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
                CreateChannels,
                ManageRoles,

                // Eco Permissions
                UseEconomy,
                ManageCurrency,
                ManageEcoAccounts,
                ForceTransactions,
                
                MentionAll,
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
    public static readonly PlanetPermission CreateChannels = new PlanetPermission(0x40, "Create Channels", "Allow members to create channels. They must have permission in the parent category.");
    public static readonly PlanetPermission ManageRoles = new PlanetPermission(0x80, "Manage Roles", "Allow members to manage roles.");

    // Eco Permissions
    public static readonly PlanetPermission UseEconomy = new PlanetPermission(0x100, "Use Economy", "Allow members to use the planet's economy.");
    public static readonly PlanetPermission ManageCurrency = new PlanetPermission(0x200, "Manage Currency", "Allow members to manage the planet's currency.");
    public static readonly PlanetPermission ManageEcoAccounts = new PlanetPermission(0x400, "Manage Eco Accounts", "Allow members to manage the planet's economy accounts.");
    public static readonly PlanetPermission ForceTransactions = new PlanetPermission(0x800, "Force Transactions", "Allow members to force transactions in the planet.");

    public static readonly PlanetPermission MentionAll = new PlanetPermission(0x1000, "Mention All", "Allow members to mention all roles.");
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

    public long Code { get; set; }
    public long Mask { get; set; }

    public PermissionNodeCode(long code, long mask)
    {
        this.Code = code;
        this.Mask = mask;
    }

    public PermissionState GetState(Permission permission, bool ignoreViewPerm)
    {
        // If the lowest, or view, permission is false, all other permissions are automatically false
        if ((Mask & 0x01) == 0x01 && (Code & 0x01) == 0 && !ignoreViewPerm)
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

