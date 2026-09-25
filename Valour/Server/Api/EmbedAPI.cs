using Microsoft.AspNetCore.Mvc;
using Valour.Sdk.Models.Embeds;
using Valour.Sdk.Models.Embeds.Items;
using Valour.Sdk.E2ee;
using Valour.Server.Cdn;
using Valour.Server.Workers;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.API;

public class EmbedAPI : BaseAPI
{
    public new static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/embed/interact", Interaction);
        app.MapPost("api/embed/update", Update);
    }

    /// <summary>
    /// Relays a live embed update from a bot to clients. Sent to a single
    /// user when TargetUserId is set, otherwise to the whole channel. The
    /// update is encrypted with the channel key; recipients' apps decrypt it
    /// and check the embed the way the server checks embeds it can read.
    /// </summary>
    private static async Task<IResult> Update([FromBody] EmbedUpdate update, ValourDb db, CoreHubService hubService, UserService userService, PlanetMemberService memberService)
    {
        if (update is null)
            return Results.BadRequest("Invalid update payload.");

        if (update.Encrypted is null || update.Encrypted.Length > EmbedUpdateCrypto.MaxEncryptedLength)
            return Results.BadRequest("Embed updates must be encrypted with the channel key.");

        var botUser = await userService.GetCurrentUserAsync();
        if (botUser is null)
            return Results.Unauthorized();

        if (!botUser.Bot)
            return Results.BadRequest("Only bots can send embed updates.");

        var message = await db.Messages
            .FirstOrDefaultAsync(x => x.Id == update.TargetMessageId)
            ?? PlanetMessageWorker.GetStagedMessage(update.TargetMessageId)?.ToDatabase();

        if (message is null)
            return Results.NotFound("Target message not found.");

        if (message.PlanetId is null)
            return Results.BadRequest("Embed updates are only supported for planet messages.");

        if (message.AuthorUserId != botUser.Id)
            return Results.BadRequest("Only the bot that sent the message can update its embed.");

        var botMember = await memberService.GetByUserAsync(botUser.Id, message.PlanetId.Value);
        if (botMember is null)
            return Results.NotFound("Bot's planet member not found.");

        var channel = (await db.Channels.FindAsync(message.ChannelId)).ToModel();
        if (!await memberService.HasPermissionAsync(botMember, channel, ChatChannelPermissions.View))
            return Results.Forbid();

        if (update.KeyGeneration < 1 || update.KeyGeneration > channel.EncryptionGeneration)
            return Results.BadRequest("The update uses an unknown channel key.");

        // Routing comes from the message, never from the request
        update.TargetChannelId = message.ChannelId;
        update.PlanetId = message.PlanetId;

        if (update.TargetUserId is not null)
            hubService.NotifyPersonalEmbedUpdateEvent(update);
        else
            hubService.NotifyChannelEmbedUpdateEvent(update);

        return Results.Ok("Sent embed update.");
    }

    /// <summary>
    /// Relays a user's embed interaction (click or form submit) to the
    /// authoring bot. All context is derived from the message server-side;
    /// the client only reports what was interacted with.
    /// </summary>
    private static async Task<IResult> Interaction([FromBody] EmbedInteractionRequest request, ValourDb db, UserService userService, CoreHubService hubService, PlanetMemberService memberService)
    {
        if (request is null)
            return Results.BadRequest("Invalid interaction payload.");

        var user = await userService.GetCurrentUserAsync();
        if (user is null)
            return Results.Unauthorized();

        var message = await db.Messages
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == request.MessageId)
            ?? PlanetMessageWorker.GetStagedMessage(request.MessageId)?.ToDatabase();

        if (message is null)
            return Results.NotFound("Target message not found.");

        if (message.PlanetId is null || message.AuthorMemberId is null)
            return Results.BadRequest("Embed interactions are only supported for planet messages.");

        // An encrypted message's embed is inside its envelope, which the
        // server cannot read, so the interaction is relayed to the author. A
        // server-sealed message from a member is history sealed after it was
        // sent; its embed can only be interactive when a bot wrote it.
        if (!HasEmbedAttachment(message) && message.EncryptionVersion != MessageEncryption.EndToEnd)
        {
            if (message.EncryptionVersion == MessageEncryption.None)
                return Results.BadRequest("Target message does not contain an embed.");

            var authorIsBot = await db.Users.AsNoTracking()
                .Where(x => x.Id == message.AuthorUserId)
                .Select(x => x.Bot)
                .FirstOrDefaultAsync();
            if (!authorIsBot)
                return Results.BadRequest("Only messages sent by bots accept embed interactions.");
        }

        var member = await memberService.GetByUserAsync(user.Id, message.PlanetId.Value);
        if (member is null)
            return Results.NotFound("Planet member not found.");

        var channel = (await db.Channels.FindAsync(message.ChannelId)).ToModel();
        if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
            return Results.Forbid();

        var formData = request.FormData;
        if (formData is not null)
        {
            if (formData.Count > 100)
                return Results.BadRequest("Too many form values.");

            foreach (var data in formData)
            {
                if (data.Value is not null && data.Value.Length > EmbedFormItem.MaxInputValueLength)
                    data.Value = data.Value[..EmbedFormItem.MaxInputValueLength];
            }
        }

        var interaction = new EmbedInteractionEvent
        {
            EventType = request.EventType,
            ElementId = request.ElementId,
            FormId = request.FormId,
            FormData = formData,
            MessageId = message.Id,
            ChannelId = message.ChannelId,
            PlanetId = message.PlanetId.Value,
            AuthorMemberId = message.AuthorMemberId.Value,
            MemberId = member.Id,
            TimeInteracted = DateTime.UtcNow,
        };

        hubService.NotifyInteractionEvent(interaction);
        return Results.Ok();
    }

    private static bool HasEmbedAttachment(Valour.Database.Message message)
    {
        return message.Attachments?.Any(x =>
            x.Type == MessageAttachmentType.Embed &&
            !string.IsNullOrWhiteSpace(x.Data)) == true;
    }
}
