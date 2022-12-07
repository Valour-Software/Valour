using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Shared.Authorization;

namespace Valour.Server.EndpointFilters;

public class ChatChannelPermissionsFilter : IEndpointFilter
{
    private readonly ValourDB _db;

    public ChatChannelPermissionsFilter(ValourDB db)
    {
        _db = db;
    }
    
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var member = ctx.HttpContext.GetMember();
        if (member is null)
            throw new Exception("ChatChannelPermsRequired attribute requires a PlanetMembershipRequired attribute.");

        var chanPermAttr =
            (ChatChannelPermsRequiredAttribute)ctx.HttpContext.Items[nameof(ChatChannelPermsRequiredAttribute)];
        
        var routeName = chanPermAttr.channelRouteName;
        if (!ctx.HttpContext.Request.RouteValues.ContainsKey(routeName))
            throw new Exception($"Could not bind route value for '{routeName}'");

        var channelId = long.Parse((string)ctx.HttpContext.Request.RouteValues[routeName]);

        var channel = await _db.PlanetChatChannels.FindAsync(channelId);

        if (channel is null)
            return ValourResult.NotFound<PlanetChatChannel>();

        foreach (var permEnum in chanPermAttr.permissions)
        {
            var perm = ChatChannelPermissions.Permissions[(int)permEnum];
            if (!await channel.HasPermissionAsync(member, perm, _db))
                return ValourResult.LacksPermission(perm);
        }

        ctx.HttpContext.Items.Add(channelId, channel);

        return await next(ctx);
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class ChatChannelPermsRequiredAttribute : Attribute
{
    public readonly ChatChannelPermissionsEnum[] permissions;
    public readonly string channelRouteName;

    public ChatChannelPermsRequiredAttribute(string channelRouteName, params ChatChannelPermissionsEnum[] permissions)
    {
        this.permissions = permissions;
        this.channelRouteName = channelRouteName;
    }

    public ChatChannelPermsRequiredAttribute(params ChatChannelPermissionsEnum[] permissions) : this("id", permissions) { }
}
