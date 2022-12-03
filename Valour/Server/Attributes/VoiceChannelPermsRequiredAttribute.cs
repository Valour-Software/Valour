using Valour.Shared.Authorization;

namespace Valour.Server.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class VoiceChannelPermsRequiredAttribute : Attribute
{
    public readonly VoiceChannelPermissionsEnum[] permissions;
    public readonly string channelRouteName;

    public VoiceChannelPermsRequiredAttribute(string channelRouteName, params VoiceChannelPermissionsEnum[] permissions)
    {
        this.permissions = permissions;
        this.channelRouteName = channelRouteName;
    }

    public VoiceChannelPermsRequiredAttribute(params VoiceChannelPermissionsEnum[] permissions) : this("id", permissions) { }
}
