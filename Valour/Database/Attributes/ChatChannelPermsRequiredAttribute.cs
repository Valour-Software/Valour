using Valour.Shared.Authorization;

namespace Valour.Database.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class ChatChannelPermsRequiredAttribute : Attribute
{
    public ChatChannelPermissionsEnum[] permissions;
    public string channelRouteName;

    public ChatChannelPermsRequiredAttribute(string channelRouteName, params ChatChannelPermissionsEnum[] permissions)
    {
        this.permissions = permissions;
        this.channelRouteName = channelRouteName;
    }

    public ChatChannelPermsRequiredAttribute(params ChatChannelPermissionsEnum[] permissions) : this("id", permissions) { }
}
