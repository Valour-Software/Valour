using Valour.Server.Database;
using Valour.Database;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Microsoft.AspNetCore.SignalR;
using Valour.Server.Hubs;

namespace Valour.Server.Services;

public sealed class PlatformBannerService
{
    private readonly ValourDb _db;
    private readonly IHubContext<CoreHub> _hub;

    public PlatformBannerService(ValourDb db, IHubContext<CoreHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<PlatformBanner> GetAsync()
    {
        var entity = await _db.PlatformBannerConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == PlatformBannerConfiguration.SingletonId && x.IsActive);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<TaskResult<PlatformBanner>> SetAsync(SetPlatformBannerRequest request, long staffUserId)
    {
        var title = request?.Title?.Trim();
        var message = request?.Message?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 80)
            return TaskResult<PlatformBanner>.FromFailure("Title must be between 1 and 80 characters.");
        if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
            return TaskResult<PlatformBanner>.FromFailure("Message must be between 1 and 500 characters.");
        if (!Enum.IsDefined(request.Kind))
            return TaskResult<PlatformBanner>.FromFailure("Invalid banner type.");

        var entity = await _db.PlatformBannerConfigurations
            .FirstOrDefaultAsync(x => x.Id == PlatformBannerConfiguration.SingletonId);
        if (entity is null)
        {
            entity = new PlatformBannerConfiguration();
            _db.PlatformBannerConfigurations.Add(entity);
        }

        entity.IsActive = true;
        entity.Title = title;
        entity.Message = message;
        entity.Kind = (int)request.Kind;
        entity.UpdatedByUserId = staffUserId;
        entity.UpdatedAt = DateTime.UtcNow;
        AddAudit(staffUserId, StaffActionType.SetPlatformBanner, "Set platform banner",
            $"Kind={request.Kind}; Hash={PlatformBanner.ComputeHash(title, message, request.Kind)}");
        await _db.SaveChangesAsync();

        var model = ToModel(entity);
        await _hub.Clients.All.SendAsync("PlatformBanner-Update", new PlatformBannerUpdate { Banner = model });
        return TaskResult<PlatformBanner>.FromData(model);
    }

    public async Task<TaskResult> ClearAsync(long staffUserId)
    {
        var entity = await _db.PlatformBannerConfigurations
            .FirstOrDefaultAsync(x => x.Id == PlatformBannerConfiguration.SingletonId);
        if (entity is not null)
        {
            entity.IsActive = false;
            entity.UpdatedByUserId = staffUserId;
            entity.UpdatedAt = DateTime.UtcNow;
        }

        AddAudit(staffUserId, StaffActionType.ClearPlatformBanner, "Cleared platform banner");
        await _db.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("PlatformBanner-Update", new PlatformBannerUpdate());
        return TaskResult.SuccessResult;
    }

    private void AddAudit(long staffUserId, StaffActionType action, string reason, string details = null)
    {
        _db.StaffAuditLogs.Add(new StaffAuditLog
        {
            Id = IdManager.Generate(),
            StaffUserId = staffUserId,
            ActionType = action,
            Reason = reason,
            Details = details,
            TimeCreated = DateTime.UtcNow
        });
    }

    private static PlatformBanner ToModel(PlatformBannerConfiguration entity)
    {
        var kind = Enum.IsDefined(typeof(PlatformBannerKind), entity.Kind)
            ? (PlatformBannerKind)entity.Kind
            : PlatformBannerKind.Information;
        return new PlatformBanner
        {
            Title = entity.Title,
            Message = entity.Message,
            Kind = kind,
            UpdatedAt = entity.UpdatedAt,
            Hash = PlatformBanner.ComputeHash(entity.Title, entity.Message, kind)
        };
    }
}
