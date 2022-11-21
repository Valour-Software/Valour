using IdGen;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Valour.Api.Items.Messages;
using Valour.Api.Items.Messages.Embeds;
using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Workers;
using Valour.Shared.Authorization;

namespace Valour.Server.API;
public class EmbedAPI : BaseAPI
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/embed/interact", Interaction);
        app.MapPost("api/embed/planetpersonalupdate", PlanetPersonalUpdate);
        app.MapPost("api/embed/planetchannelupdate", PlanetChannelUpdate);
    }

    private static async Task<IResult> PlanetChannelUpdate(HttpContext ctx, ValourDB db, [FromHeader] string authorization)
    {
        var ceu = await JsonSerializer.DeserializeAsync<ChannelEmbedUpdate>(ctx.Request.Body);

        if (ceu.NewEmbedContent.Length > 65535)
        {
            return Results.BadRequest("EmbedData must be under 65535 chars");
        }

        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken == null) { await TokenInvalid(ctx); return Results.BadRequest(); }

        var targetmessage = await db.PlanetMessages.Include(x => x.AuthorMember).FirstOrDefaultAsync(x => x.Id == ceu.TargetMessageId);
        if (targetmessage is null)
        {
            targetmessage = PlanetMessageWorker.GetStagedMessage(ceu.TargetMessageId);
            if (targetmessage is null)
                return Results.NotFound("Target message not found");
        }

        var botmember = await db.PlanetMembers.Include(x => x.User).FirstOrDefaultAsync(x => x.UserId == authToken.UserId && x.PlanetId == targetmessage.PlanetId);

        if (botmember is null)
            return Results.NotFound("Bot's member not found");

        if (!botmember.User.Bot)
            return Results.BadRequest("Only bots can do personal embed updates!");

        // only the bot who sent the message can send channel embed updates
        if (targetmessage.AuthorUserId != botmember.UserId)
            return Results.BadRequest("User id mismatch");

        if (targetmessage.PlanetId != botmember.PlanetId)
            return Results.BadRequest("Planet id mismatch");

        if (targetmessage.EmbedData == null || targetmessage.EmbedData == "")
            return Results.BadRequest("Target message does not contain an embed!");

        var channel = await db.PlanetChatChannels.FindAsync(targetmessage.ChannelId);

        if (!await channel.HasPermissionAsync(botmember, ChatChannelPermissions.View, db))
            return Results.BadRequest("Member lacks ChatChannelPermissions.View");

        channel = await db.PlanetChatChannels.FindAsync(ceu.TargetChannelId);

        if (!await channel.HasPermissionAsync(botmember, ChatChannelPermissions.View, db))
            return Results.BadRequest("Member lacks ChatChannelPermissions.View");

        PlanetHub.NotifyChannelEmbedUpdateEvent(ceu);

        return Results.Ok("Sent Channel Embed Update");
    }

    private static async Task<IResult> PlanetPersonalUpdate(HttpContext ctx, ValourDB db, [FromHeader] string authorization)
    {
        var peu = await JsonSerializer.DeserializeAsync<PersonalEmbedUpdate>(ctx.Request.Body);

        if (peu.NewEmbedContent.Length > 65535) {
            return Results.BadRequest("EmbedData must be under 65535 chars");
        }

        var authToken = await AuthToken.TryAuthorize(authorization, db);
        if (authToken == null) { await TokenInvalid(ctx); return Results.BadRequest(); }

        var targetmessage = await db.PlanetMessages.Include(x => x.AuthorMember).FirstOrDefaultAsync(x => x.Id == peu.TargetMessageId);
        if (targetmessage is null)
        {
            targetmessage = PlanetMessageWorker.GetStagedMessage(peu.TargetMessageId);
            if (targetmessage is null)
                return Results.NotFound("Target message not found");
        }

        var botmember = await db.PlanetMembers.Include(x => x.User).FirstOrDefaultAsync(x => x.UserId == authToken.UserId && x.PlanetId == targetmessage.PlanetId);

        if (botmember is null)
            return Results.NotFound("Bot's member not found");

        if (!botmember.User.Bot)
            return Results.BadRequest("Only bots can do personal embed updates!");

        var targetmember = await db.PlanetMembers.FirstOrDefaultAsync(x => x.UserId == peu.TargetUserId && x.PlanetId == targetmessage.PlanetId);
        if (targetmember is null)
            return Results.NotFound("Target member not found");

        // only the bot who sent the message can send personal embed updates
        if (targetmessage.AuthorUserId != botmember.UserId)
            return Results.BadRequest("Member id mismatch");

        if (targetmessage.PlanetId != botmember.PlanetId)
            return Results.BadRequest("Planet id mismatch");

        if (targetmessage.EmbedData == null || targetmessage.EmbedData == "")
            return Results.BadRequest("Target message does not contain an embed!");

        var channel = await db.PlanetChatChannels.FindAsync(targetmessage.ChannelId);

        if (!await channel.HasPermissionAsync(botmember, ChatChannelPermissions.View, db))
            return Results.BadRequest("Member lacks ChatChannelPermissions.View");

        PlanetHub.NotifyPersonalEmbedUpdateEvent(peu);

        return Results.Ok("Sent Personal Embed Update");
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
