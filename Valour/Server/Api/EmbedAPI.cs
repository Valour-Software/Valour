using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Server.Workers;
using Valour.Shared.Authorization;
using Valour.Sdk.Models.Messages.Embeds.Items;
using Valour.Server.Cdn;

namespace Valour.Server.API;
public class EmbedAPI : BaseAPI
{
    public new static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/embed/interact", Interaction);
        app.MapPost("api/embed/planetpersonalupdate", PlanetPersonalUpdate);
        app.MapPost("api/embed/planetchannelupdate", PlanetChannelUpdate);
    }

    private static async Task<IResult> PlanetChannelUpdate(HttpContext ctx, ValourDB db, CoreHubService hubService, UserService userService, PlanetMemberService memberService, [FromHeader] string authorization)
    {
        var ceu = await JsonSerializer.DeserializeAsync<ChannelEmbedUpdate>(ctx.Request.Body);

        if (ceu.NewEmbedContent.Length > 65535)
            return Results.BadRequest("EmbedData must be under 65535 chars");

        // load embed to check for anti-valour propaganda (incorrect media URIs)
        var embed = JsonSerializer.Deserialize<Embed>(ceu.NewEmbedContent);
        foreach (var page in embed.Pages)
        {
            foreach (var item in page.GetAllItems())
            {
                if (item.ItemType == Valour.Sdk.Models.Messages.Embeds.Items.EmbedItemType.Media)
                {
                    var at = ((EmbedMediaItem)item).Attachment;
                    var result = MediaUriHelper.ScanMediaUri(at);
                    if (!result.Success)
                        return Results.BadRequest(result.Message);
                }
            }
        }

        var botUser = await userService.GetCurrentUserAsync();
        if (botUser is null) { await TokenInvalid(ctx); return Results.BadRequest(); }

        var targetmessage = await db.Messages.Include(x => x.AuthorMember).FirstOrDefaultAsync(x => x.Id == ceu.TargetMessageId);
        if (targetmessage is null)
        {
            targetmessage = PlanetMessageWorker.GetStagedMessage(ceu.TargetMessageId).ToDatabase();
            if (targetmessage is null)
                return Results.NotFound("Target message not found");
        }

        // TODO: Jacob check this change for PlanetId nullability
        var botmember = await memberService.GetByUserAsync(botUser.Id, targetmessage.PlanetId.Value);

        if (botmember is null)
            return Results.NotFound("Bot's member not found");

        if (!botUser.Bot)
            return Results.BadRequest("Only bots can do personal embed updates!");

        // only the bot who sent the message can send channel embed updates
        if (targetmessage.AuthorUserId != botmember.UserId)
            return Results.BadRequest("User id mismatch");

        if (targetmessage.PlanetId != botmember.PlanetId)
            return Results.BadRequest("Planet id mismatch");

        if (targetmessage.EmbedData == null || targetmessage.EmbedData == "")
            return Results.BadRequest("Target message does not contain an embed!");

        var channel = (await db.Channels.FindAsync(targetmessage.ChannelId)).ToModel();

        if (!await memberService.HasPermissionAsync(botmember, channel, ChatChannelPermissions.View))
            return Results.BadRequest("Member lacks ChatChannelPermissions.View");

        channel = (await db.Channels.FindAsync(ceu.TargetChannelId)).ToModel();

        if (!await memberService.HasPermissionAsync(botmember, channel, ChatChannelPermissions.View))
            return Results.BadRequest("Member lacks ChatChannelPermissions.View");

        hubService.NotifyChannelEmbedUpdateEvent(ceu);

        return Results.Ok("Sent Channel Embed Update");
    }

    private static async Task<IResult> PlanetPersonalUpdate(HttpContext ctx, ValourDB db, UserService userService, CoreHubService hubService, PlanetMemberService memberService, [FromHeader] string authorization)
    {
        var peu = await JsonSerializer.DeserializeAsync<PersonalEmbedUpdate>(ctx.Request.Body);

        if (peu.NewEmbedContent is not null && peu.NewEmbedContent.Length > 65535)
            return Results.BadRequest("EmbedData must be under 65535 chars");

        if (peu.ChangedEmbedItemsContent is not null && peu.ChangedEmbedItemsContent.Length > 65535)
            return Results.BadRequest("ChangeItemsData must be under 65535 chars");

        if (peu.NewEmbedContent is not null)
        {
            // load embed to check for anti-valour propaganda (incorrect media URIs)
            var embed = JsonSerializer.Deserialize<Embed>(peu.NewEmbedContent);
            foreach (var page in embed.Pages)
            {
                foreach (var item in page.GetAllItems())
                {
                    if (item.ItemType == Valour.Sdk.Models.Messages.Embeds.Items.EmbedItemType.Media)
                    {
                        var at = ((EmbedMediaItem)item).Attachment;
                        var result = MediaUriHelper.ScanMediaUri(at);
                        if (!result.Success)
                            return Results.BadRequest(result.Message);
                    }
                }
            }
        }
        else
        {
            var embeditems = JsonSerializer.Deserialize<List<EmbedItem>>(peu.ChangedEmbedItemsContent);
            foreach (var embeditem in embeditems)
            {
                foreach (var item in embeditem.GetAllItems())
                {
                    if (item.ItemType == Valour.Sdk.Models.Messages.Embeds.Items.EmbedItemType.Media)
                    {
                        var at = ((EmbedMediaItem)item).Attachment;
                        var result = MediaUriHelper.ScanMediaUri(at);
                        if (!result.Success)
                            return Results.BadRequest(result.Message);
                    }
                }
            }
        }

        var botUser = await userService.GetCurrentUserAsync();
        if (botUser is null) { await TokenInvalid(ctx); return Results.BadRequest(); }

        var targetmessage = await db.Messages.Include(x => x.AuthorMember).FirstOrDefaultAsync(x => x.Id == peu.TargetMessageId);
        if (targetmessage is null)
        {
            targetmessage = PlanetMessageWorker.GetStagedMessage(peu.TargetMessageId).ToDatabase();
            if (targetmessage is null)
                return Results.NotFound("Target message not found");
        }

        // TODO: And here
        var botmember = await memberService.GetByUserAsync(botUser.Id, targetmessage.PlanetId.Value);

        if (botmember is null)
            return Results.NotFound("Bot's member not found");

        if (!botUser.Bot)
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

        var channel = (await db.Channels.FindAsync(targetmessage.ChannelId)).ToModel();

        if (!await memberService.HasPermissionAsync(botmember, channel, ChatChannelPermissions.View))
            return Results.BadRequest("Member lacks ChatChannelPermissions.View");

        hubService.NotifyPersonalEmbedUpdateEvent(peu);

        return Results.Ok("Sent Personal Embed Update");
    }

    private static async Task Interaction(HttpContext ctx, ValourDB db, UserService userService, CoreHubService hubService, PlanetMemberService memberService, [FromHeader] string authorization)
    {
        EmbedInteractionEvent e = await JsonSerializer.DeserializeAsync<EmbedInteractionEvent>(ctx.Request.Body);

        var botUser = await userService.GetCurrentUserAsync();
        if (botUser is null) { await TokenInvalid(ctx); return; }

        var member = await db.PlanetMembers.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Id == e.MemberId);
        if (member == null) { await NotFound("Member not found", ctx); return; }
        if (botUser.Id != member.UserId) { await BadRequest("Member id mismatch", ctx); return; }

        var channel = await db.Channels.FindAsync(e.ChannelId);

        if (!await memberService.HasPermissionAsync(member.ToModel(), channel.ToModel(), ChatChannelPermissions.View)) { await Unauthorized("Member lacks ChatChannelPermissions.View", ctx); return; }

        hubService.NotifyInteractionEvent(e);
    }
}
