using System.Security.Cryptography;
using System.Text;
using Valour.Sdk.Models.Embeds;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using SdkMessageAttachment = Valour.Sdk.Models.MessageAttachment;
using WebhookExecuteRequest = Valour.Sdk.Models.WebhookExecuteRequest;
using WebhookMessageEditRequest = Valour.Sdk.Models.WebhookMessageEditRequest;

namespace Valour.Server.Services;

public class PlanetWebhookService
{
    public const int MaxWebhooksPerPlanet = 50;

    private readonly ValourDb _db;
    private readonly ILogger<PlanetWebhookService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly MessageService _messageService;

    public PlanetWebhookService(
        ValourDb db,
        ILogger<PlanetWebhookService> logger,
        CoreHubService coreHub,
        MessageService messageService)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
        _messageService = messageService;
    }

    public async Task<PlanetWebhook> GetAsync(long id) =>
        (await _db.PlanetWebhooks.FindAsync(id)).ToModel();

    public async Task<List<PlanetWebhook>> GetAllForPlanetAsync(long planetId) =>
        await _db.PlanetWebhooks.AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .OrderBy(x => x.Id)
            .Select(x => x.ToModel())
            .ToListAsync();

    /// <summary>
    /// Loads a webhook only if the given token matches, using a fixed-time
    /// comparison. Returns null for unknown id or wrong token alike.
    /// </summary>
    public async Task<PlanetWebhook> AuthenticateAsync(long id, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var webhook = await _db.PlanetWebhooks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (webhook?.Token is null)
            return null;

        var stored = Encoding.UTF8.GetBytes(webhook.Token);
        var given = Encoding.UTF8.GetBytes(token);

        if (stored.Length != given.Length || !CryptographicOperations.FixedTimeEquals(stored, given))
            return null;

        return webhook.ToModel();
    }

    public async Task<TaskResult<PlanetWebhook>> CreateAsync(PlanetWebhook webhook, PlanetMember creator)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, creator?.PlanetId);
        if (!migrationGuard.Success)
            return new(false, migrationGuard.Message);

        var profileResult = ValidateProfile(webhook.Name, webhook.AvatarUrl, nameRequired: true);
        if (!profileResult.Success)
            return new(false, profileResult.Message);

        var channelResult = await ValidateChannelAsync(creator.PlanetId, webhook.ChannelId);
        if (!channelResult.Success)
            return new(false, channelResult.Message);

        var count = await _db.PlanetWebhooks.CountAsync(x => x.PlanetId == creator.PlanetId);
        if (count >= MaxWebhooksPerPlanet)
            return new(false, $"Planets are limited to {MaxWebhooksPerPlanet} webhooks.");

        webhook.Id = IdManager.Generate();
        webhook.PlanetId = creator.PlanetId;
        webhook.CreatorUserId = creator.UserId;
        webhook.Token = GenerateToken();
        webhook.TimeCreated = DateTime.UtcNow;

        try
        {
            await _db.PlanetWebhooks.AddAsync(webhook.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create webhook");
            return new(false, "Failed to create webhook.");
        }

        // Broadcasts reach all planet members; never include the token
        _coreHub.NotifyPlanetItemChange(webhook.WithoutToken());

        return new(true, "Success", webhook);
    }

    public async Task<TaskResult<PlanetWebhook>> UpdateAsync(PlanetWebhook updated)
    {
        var old = await _db.PlanetWebhooks.FindAsync(updated.Id);
        if (old is null)
            return new(false, "Webhook not found.");

        var migrationGuard = await MigrationLock.GuardAsync(_db, old.PlanetId);
        if (!migrationGuard.Success)
            return new(false, migrationGuard.Message);

        var profileResult = ValidateProfile(updated.Name, updated.AvatarUrl, nameRequired: true);
        if (!profileResult.Success)
            return new(false, profileResult.Message);

        if (updated.ChannelId != old.ChannelId)
        {
            var channelResult = await ValidateChannelAsync(old.PlanetId, updated.ChannelId);
            if (!channelResult.Success)
                return new(false, channelResult.Message);
        }

        old.Name = updated.Name;
        old.AvatarUrl = updated.AvatarUrl;
        old.ChannelId = updated.ChannelId;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update webhook");
            return new(false, "Failed to update webhook.");
        }

        var model = old.ToModel();
        _coreHub.NotifyPlanetItemChange(model.WithoutToken());

        return new(true, "Success", model.WithoutToken());
    }

    public async Task<TaskResult> DeleteAsync(long id)
    {
        var webhook = await _db.PlanetWebhooks.FindAsync(id);
        if (webhook is null)
            return new(false, "Webhook not found.");

        var migrationGuard = await MigrationLock.GuardAsync(_db, webhook.PlanetId);
        if (!migrationGuard.Success)
            return new(false, migrationGuard.Message);

        try
        {
            _db.PlanetWebhooks.Remove(webhook);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete webhook");
            return new(false, "Failed to delete webhook.");
        }

        _coreHub.NotifyPlanetItemDelete(webhook.ToModel().WithoutToken());

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Replaces the webhook's token, invalidating the old one.
    /// </summary>
    public async Task<TaskResult<PlanetWebhook>> RotateTokenAsync(long id)
    {
        var webhook = await _db.PlanetWebhooks.FindAsync(id);
        if (webhook is null)
            return new(false, "Webhook not found.");

        var migrationGuard = await MigrationLock.GuardAsync(_db, webhook.PlanetId);
        if (!migrationGuard.Success)
            return new(false, migrationGuard.Message);

        webhook.Token = GenerateToken();

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to rotate webhook token");
            return new(false, "Failed to rotate webhook token.");
        }

        return new(true, "Success", webhook.ToModel());
    }

    /// <summary>
    /// Posts a message to the webhook's channel. The webhook must already be
    /// authenticated; no channel permissions are re-checked here because the
    /// channel binding was authorized by a ManageWebhooks holder.
    /// </summary>
    public async Task<TaskResult<Message>> ExecuteAsync(PlanetWebhook webhook, WebhookExecuteRequest request)
    {
        if (request is null)
            return new(false, "Include a request body.");

        var profileResult = ValidateProfile(request.OverrideName, request.OverrideAvatarUrl, nameRequired: false);
        if (!profileResult.Success)
            return new(false, profileResult.Message);

        var attachments = BuildAttachments(request.Embeds, request.Attachments, out var attachmentError);
        if (attachmentError is not null)
            return new(false, attachmentError);

        if (string.IsNullOrWhiteSpace(request.Content) && (attachments is null || attachments.Count == 0))
            return new(false, "Message must include content, embeds, or attachments.");

        if (request.ReplyToId is not null)
        {
            var replyTo = (await _db.Messages.AsNoTracking()
                              .FirstOrDefaultAsync(x => x.Id == request.ReplyToId)).ToModel()
                          ?? Workers.PlanetMessageWorker.GetStagedMessage(request.ReplyToId.Value);

            if (replyTo is null || replyTo.ChannelId != webhook.ChannelId)
                return new(false, "ReplyToId must reference a message in the webhook's channel.");
        }

        var message = new Message
        {
            ChannelId = webhook.ChannelId,
            PlanetId = webhook.PlanetId,
            AuthorUserId = ISharedUser.VictorUserId,
            AuthorMemberId = null,
            Content = request.Content,
            ReplyToId = request.ReplyToId,
            Fingerprint = Guid.NewGuid().ToString(),
            Attachments = attachments,
        };

        var writeOptions = new MessageWriteOptions
        {
            WebhookId = webhook.Id,
            OverrideName = request.OverrideName ?? webhook.Name,
            OverrideAvatarUrl = request.OverrideAvatarUrl ?? webhook.AvatarUrl,
            SuppressRoleMentions = true,
        };

        return await _messageService.PostMessageAsync(message, writeOptions);
    }

    /// <summary>
    /// Edits a message previously sent by this webhook.
    /// </summary>
    public async Task<TaskResult<Message>> EditMessageAsync(PlanetWebhook webhook, long messageId, WebhookMessageEditRequest request)
    {
        if (request is null)
            return new(false, "Include a request body.");

        var ownership = await GetOwnedMessageAsync(webhook, messageId);
        if (!ownership.Success)
            return new(false, ownership.Message);

        var old = ownership.Data;

        List<SdkMessageAttachment> attachments;
        if (request.Embeds is not null)
        {
            attachments = BuildAttachments(request.Embeds, null, out var attachmentError);
            if (attachmentError is not null)
                return new(false, attachmentError);
        }
        else
        {
            attachments = old.Attachments?.Where(x => !x.Inline).ToList();
        }

        var updated = new Message
        {
            Id = messageId,
            Content = request.Content ?? old.Content,
            Attachments = attachments,
        };

        return await _messageService.EditMessageAsync(updated);
    }

    /// <summary>
    /// Deletes a message previously sent by this webhook.
    /// </summary>
    public async Task<TaskResult> DeleteMessageAsync(PlanetWebhook webhook, long messageId)
    {
        var ownership = await GetOwnedMessageAsync(webhook, messageId);
        if (!ownership.Success)
            return new(false, ownership.Message);

        return await _messageService.DeleteMessageAsync(messageId);
    }

    private async Task<TaskResult<Message>> GetOwnedMessageAsync(PlanetWebhook webhook, long messageId)
    {
        var message = (await _db.Messages.AsNoTracking()
                          .Include(x => x.Attachments)
                          .FirstOrDefaultAsync(x => x.Id == messageId)).ToModel()
                      ?? Workers.PlanetMessageWorker.GetStagedMessage(messageId);

        if (message is null || message.WebhookId != webhook.Id)
            return new(false, "Message not found for this webhook.");

        return new(true, "Success", message);
    }

    private static List<SdkMessageAttachment> BuildAttachments(List<string> embeds, List<SdkMessageAttachment> extra, out string error)
    {
        error = null;
        List<SdkMessageAttachment> attachments = null;

        if (embeds is not null)
        {
            if (embeds.Count > Valour.Sdk.Models.WebhookExecuteRequest.MaxEmbeds)
            {
                error = $"A message may include at most {Valour.Sdk.Models.WebhookExecuteRequest.MaxEmbeds} embeds.";
                return null;
            }

            attachments = new();
            foreach (var embedJson in embeds)
            {
                if (string.IsNullOrWhiteSpace(embedJson))
                    continue;

                var embed = EmbedParser.TryParse(embedJson);
                if (embed is null)
                {
                    error = "Embed data is invalid.";
                    return null;
                }

                var valid = EmbedParser.Validate(embed);
                if (!valid.Success)
                {
                    error = valid.Message;
                    return null;
                }

                var attachment = new SdkMessageAttachment(MessageAttachmentType.Embed);
                attachment.SetEmbedPayload(embedJson);
                attachments.Add(attachment);
            }
        }

        if (extra is not null)
        {
            attachments ??= new();
            foreach (var attachment in extra.Where(x => x is not null))
            {
                // Inline previews are server-generated only
                attachment.Inline = false;
                attachments.Add(attachment);
            }
        }

        return attachments;
    }

    private static TaskResult ValidateProfile(string name, string avatarUrl, bool nameRequired)
    {
        if (nameRequired && string.IsNullOrWhiteSpace(name))
            return TaskResult.FromFailure("Webhook name is required.");

        if (name is not null && name.Length > ISharedPlanetWebhook.MaxNameLength)
            return TaskResult.FromFailure($"Name must be {ISharedPlanetWebhook.MaxNameLength} characters or fewer.");

        if (avatarUrl is not null)
        {
            if (avatarUrl.Length > ISharedPlanetWebhook.MaxAvatarUrlLength)
                return TaskResult.FromFailure($"Avatar URL must be {ISharedPlanetWebhook.MaxAvatarUrlLength} characters or fewer.");

            if (!Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri) ||
                !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return TaskResult.FromFailure("Avatar URL must be an absolute https URL.");
        }

        return TaskResult.SuccessResult;
    }

    private async Task<TaskResult> ValidateChannelAsync(long planetId, long channelId)
    {
        var channel = await _db.Channels.AsNoTracking().FirstOrDefaultAsync(x => x.Id == channelId);

        if (channel is null || channel.PlanetId != planetId)
            return TaskResult.FromFailure("Channel not found in this planet.");

        if (channel.ChannelType != ChannelTypeEnum.PlanetChat)
            return TaskResult.FromFailure("Webhooks can only target chat channels.");

        return TaskResult.SuccessResult;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "whk_" + Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
