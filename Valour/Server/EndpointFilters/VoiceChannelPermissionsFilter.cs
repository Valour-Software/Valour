using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Shared.Authorization;

namespace Valour.Server.EndpointFilters;

public class VoiceChannelPermissionsFilter : IEndpointFilter
{
    private readonly ValourDB _db;

    public VoiceChannelPermissionsFilter(ValourDB db)
    {
        _db = db;
    }
    
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var member = ctx.HttpContext.GetMember();
        if (member is null)
            throw new Exception("VoiceChannelPermsRequired attribute requires a PlanetMembershipRequired attribute.");

        var voicePermAttr =
            (VoiceChannelPermsRequiredAttribute)ctx.HttpContext.Items[nameof(VoiceChannelPermsRequiredAttribute)];
        
        var routeName = voicePermAttr.channelRouteName;
        if (!ctx.HttpContext.Request.RouteValues.ContainsKey(routeName))
            throw new Exception($"Could not bind route value for '{routeName}'");

        var channelId = long.Parse((string)ctx.HttpContext.Request.RouteValues[routeName]);

        var channel = await _db.PlanetVoiceChannels.FindAsync(channelId);

        if (channel is null)
            return ValourResult.NotFound<PlanetVoiceChannel>();

        foreach (var permEnum in voicePermAttr.permissions)
        {
            var perm = VoiceChannelPermissions.Permissions[(int)permEnum];
            if (!await channel.HasPermissionAsync(member, perm, _db))
                return ValourResult.LacksPermission(perm);
        }

        ctx.HttpContext.Items.Add(channelId, channel);

        return await next(ctx);
    }
}

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
