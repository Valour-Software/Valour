using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Messages.Embeds;

namespace Valour.Server.API;
public class EmbedAPI : BaseAPI
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/embed/interact", Interaction);
    }

    private static async Task Interaction(HttpContext ctx, ValourDB db, [FromHeader] string authorization)
    {
        EmbedInteractionEvent e = await JsonSerializer.DeserializeAsync<EmbedInteractionEvent>(ctx.Request.Body);

        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken == null) { await TokenInvalid(ctx); return; }

        var member = await db.PlanetMembers.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Id == e.MemberId);
        if (member == null) { await NotFound("Member not found", ctx); return; }
        if (authToken.UserId != member.UserId) { await BadRequest("Member id mismatch", ctx); return; }

        var channel = await db.PlanetChatChannels.FindAsync(e.ChannelId);

        if (!await channel.HasPermissionAsync(member, ChatChannelPermissions.View, db)) { await Unauthorized("Member lacks ChatChannelPermissions.View", ctx); return; }

        PlanetHub.NotifyInteractionEvent(e);
    }
}
