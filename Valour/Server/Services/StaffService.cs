using Valour.Server.Database;
using Valour.Server.Email;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Queries;
using MfaRemovalStatus = Valour.Database.MfaRemovalStatus;
using PendingMfaRemoval = Valour.Database.PendingMfaRemoval;

namespace Valour.Server.Services;

public class StaffService
{
    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly TokenService _tokenService;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<StaffService> _logger;

    public StaffService(ValourDb db, UserService userService, TokenService tokenService, CoreHubService coreHub, ILogger<StaffService> logger)
    {
        _db = db;
        _userService = userService;
        _tokenService = tokenService;
        _coreHub = coreHub;
        _logger = logger;
    }

    
    public async Task<List<Report>> GetReportsAsync() =>
        await _db.Reports.Select(x => x.ToModel()).ToListAsync();
    
    public async Task<QueryResponse<Report>> QueryReportsAsync(QueryRequest queryRequest)
    {
        var take = queryRequest.Take;
        if (take > 100)
            take = 100;
        
        var skip = queryRequest.Skip;
        
        var query = _db.Reports
            .AsQueryable()
            .AsNoTracking();

        var reason = queryRequest.Options?.Filters?.GetValueOrDefault("reason");
        if (int.TryParse(reason, out int reasonCode))
        {
            query = query.Where(x => (int)x.ReasonCode == reasonCode);
        }

        var resolution = queryRequest.Options?.Filters?.GetValueOrDefault("resolution");
        if (int.TryParse(resolution, out int resolutionCode))
        {
            query = query.Where(x => (int)x.Resolution == resolutionCode);
        }

        var unresolved = queryRequest.Options?.Filters?.GetValueOrDefault("unresolved");
        if (unresolved == "true")
        {
            query = query.Where(x => x.Resolution == ReportResolution.None);
        }

        var resolved = queryRequest.Options?.Filters?.GetValueOrDefault("resolved");
        if (resolved == "true")
        {
            query = query.Where(x => x.Resolution != ReportResolution.None);
        }
        
        var sort = queryRequest.Options?.Sort?.Field;
        if (sort is not null)
        {
            // TODO: Sorting
        }
        else
        {
            // Sort by time created
            query = query.OrderByDescending(x => x.TimeCreated);
        }
        
        var reports = await query
            .Skip(skip)
            .Take(take)
            .Select(x => x.ToModel())
            .ToListAsync();
        
        var total = await query.CountAsync();
        
        return new QueryResponse<Report>()
        {
            Items = reports,
            TotalCount = total
        };
    }

    public async Task<TaskResult> SetReportReviewedAsync(string reportId, bool value)
    {
        var report = await _db.Reports.FindAsync(reportId);
        if (report is null)
            return TaskResult.FromFailure("Report not found");
        
        report.Reviewed = value;
        
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Writes a row to the platform staff audit log. Every sensitive staff
    /// action goes through here.
    /// </summary>
    public async Task LogActionAsync(long staffUserId, StaffActionType action, long? targetUserId, string reason, string details = null)
    {
        _db.StaffAuditLogs.Add(new Valour.Database.StaffAuditLog
        {
            Id = IdManager.Generate(),
            StaffUserId = staffUserId,
            ActionType = action,
            TargetUserId = targetUserId,
            Reason = reason ?? string.Empty,
            Details = details,
            TimeCreated = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }

    public async Task<QueryResponse<StaffAuditLogEntry>> QueryAuditLogAsync(QueryRequest queryRequest)
    {
        var take = queryRequest.Take;
        if (take > 100)
            take = 100;

        IQueryable<Valour.Database.StaffAuditLog> query = _db.StaffAuditLogs
            .AsNoTracking()
            .OrderByDescending(x => x.TimeCreated);

        var target = queryRequest.Options?.Filters?.GetValueOrDefault("target");
        if (long.TryParse(target, out var targetId))
            query = query.Where(x => x.TargetUserId == targetId);

        var total = await query.CountAsync();

        var items = await (
            from log in query.Skip(queryRequest.Skip).Take(take)
            join staffUser in _db.Users on log.StaffUserId equals staffUser.Id into staffGroup
            from staffUser in staffGroup.DefaultIfEmpty()
            join targetUser in _db.Users on log.TargetUserId equals targetUser.Id into targetGroup
            from targetUser in targetGroup.DefaultIfEmpty()
            select new StaffAuditLogEntry
            {
                Id = log.Id,
                StaffUserId = log.StaffUserId,
                StaffName = staffUser == null ? null : staffUser.Name + "#" + staffUser.Tag,
                ActionType = log.ActionType,
                TargetUserId = log.TargetUserId,
                TargetName = targetUser == null ? null : targetUser.Name + "#" + targetUser.Tag,
                Reason = log.Reason,
                Details = log.Details,
                TimeCreated = log.TimeCreated
            }).ToListAsync();

        return new QueryResponse<StaffAuditLogEntry>()
        {
            Items = items,
            TotalCount = total
        };
    }

    /// <summary>
    /// Removes all auth tokens for a user and evicts them from cache,
    /// forcing every session to log in again.
    /// </summary>
    private async Task InvalidateSessionsAsync(long userId)
    {
        // Eviction must happen after the delete is committed, otherwise a
        // concurrent request can re-cache the token from the still-live DB row.
        var tokens = await _db.AuthTokens.Where(x => x.UserId == userId).ToListAsync();
        _db.AuthTokens.RemoveRange(tokens);

        await _db.SaveChangesAsync();

        foreach (var token in tokens)
            _tokenService.RemoveFromQuickCache(token.Id);

        _coreHub.ForceLogoutUser(userId);
    }

    public async Task<TaskResult> DisableUserAsync(long userId, bool value, long staffUserId, string reason)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult.FromFailure("Account not found", errorCode: 404);

        user.Disabled = value;

        await _db.SaveChangesAsync();

        await InvalidateSessionsAsync(userId);

        await LogActionAsync(staffUserId,
            value ? StaffActionType.DisableAccount : StaffActionType.EnableAccount,
            userId, reason, $"{user.Name}#{user.Tag}");

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> DeleteUserAsync(long userId, long staffUserId, string reason)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult.FromFailure("Account not found", errorCode: 404);

        // Deleting staff accounts requires removing their staff status first.
        // Keeps a compromised staff account from wiping the rest of the team.
        if (user.ValourStaff)
            return TaskResult.FromFailure("Staff accounts cannot be deleted with this tool.");

        var identity = $"{user.Name}#{user.Tag}";

        try
        {
            var result = await _userService.HardDelete(user.ToModel());
            if (!result.Success)
                return TaskResult.FromFailure(result.Message);
        }
        catch (Exception e)
        {
            return TaskResult.FromFailure(e.Message);
        }

        await LogActionAsync(staffUserId, StaffActionType.DeleteAccount, userId, reason, identity);

        return TaskResult.SuccessResult;
    }

    public async Task<Message> GetMessageAsync(long messageId)
    {
        var msg = await _db.Messages.FirstOrDefaultAsync(x => x.Id == messageId);
        return msg.ToModel();
    }

    public async Task<Report> GetReportAsync(string reportId)
    {
        var report = await _db.Reports.FindAsync(reportId);
        return report?.ToModel();
    }

    public async Task<TaskResult> ResolveReportAsync(string reportId, ReportResolution resolution, string staffNotes, long staffUserId)
    {
        var report = await _db.Reports.FindAsync(reportId);
        if (report is null)
            return TaskResult.FromFailure("Report not found");

        report.Resolution = resolution;
        report.StaffNotes = staffNotes;
        report.ResolvedById = staffUserId;
        report.ResolvedAt = DateTime.UtcNow;
        report.Reviewed = true;

        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> SendMassEmailAsync(string subject, string htmlBody, string baseUrl)
    {
        // These are administrative emails (ToS/privacy updates) — all verified users must receive them.
        // Unsubscribe headers are included for deliverability but don't gate sending.
        var recipients = await _db.PrivateInfos
            .Where(x => x.Verified)
            .Select(x => new { x.Email, x.UserId })
            .ToListAsync();

        if (recipients.Count == 0)
            return TaskResult.FromFailure("No verified emails found.");

        var bodyContent = $@"
                <h1 style='color: #333;'>{subject}</h1>
                {htmlBody}
                <p style='color: #666;'>— Valour Team</p>";

        var sent = 0;
        var batchCount = 0;
        foreach (var recipient in recipients)
        {
            try
            {
                var token = UnsubscribeTokenService.GenerateToken(recipient.UserId);
                var unsubscribeUrl = $"{baseUrl}/api/email/unsubscribe?token={token}";
                var oneclickUrl = $"{baseUrl}/api/email/unsubscribe/oneclick?token={token}";

                var wrappedHtml = EmailTemplateHelper.WrapInTemplate(bodyContent, unsubscribeUrl);

                await EmailManager.SendMarketingEmailAsync(
                    recipient.Email,
                    subject,
                    htmlBody,
                    wrappedHtml,
                    oneclickUrl);

                sent++;
                batchCount++;

                // Rate limiting: ~10 emails/sec
                await Task.Delay(100);

                // Extra pause every 100 emails to protect sender reputation
                if (batchCount >= 100)
                {
                    batchCount = 0;
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send mass email to {Email}", recipient.Email);
            }
        }

        _logger.LogInformation("Mass email sent to {Sent}/{Total} eligible users.", sent, recipients.Count);

        return TaskResult.FromSuccess($"Email sent to {sent} of {recipients.Count} eligible users.");
    }

    /// <summary>
    /// Resolves a user (and their private info when present) from a user id,
    /// email, username#tag, or bare username. Shared by staff verify and
    /// staff lookup. Returns a failed TaskResult in Error when unresolved.
    /// </summary>
    private async Task<(Valour.Database.User User, Valour.Database.UserPrivateInfo PrivateInfo, TaskResult Error)>
        ResolveUserByIdentifierAsync(string identifier)
    {
        identifier = identifier?.Trim();
        if (string.IsNullOrWhiteSpace(identifier))
            return (null, null, TaskResult.FromFailure("Please provide a user id, username, username#tag, or email.", errorCode: 400));

        Valour.Database.User user = null;
        Valour.Database.UserPrivateInfo privateInfo = null;

        if (identifier.Contains('@'))
        {
            var sanitized = UserUtils.SanitizeEmail(identifier);
            var validEmail = UserUtils.TestEmail(sanitized);
            if (!validEmail.Success)
                return (null, null, TaskResult.FromFailure("Invalid email format.", errorCode: 400));

            var normalizedEmail = validEmail.Data.ToLower();
            privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);
            if (privateInfo is null)
                return (null, null, TaskResult.FromFailure("No account found with that email.", errorCode: 404));

            user = await _db.Users.FindAsync(privateInfo.UserId);
        }
        else if (identifier.Contains('#'))
        {
            var split = identifier.Split('#', 2);
            if (split.Length != 2 || string.IsNullOrWhiteSpace(split[0]) || string.IsNullOrWhiteSpace(split[1]))
                return (null, null, TaskResult.FromFailure("Invalid format. Use username#tag.", errorCode: 400));

            var username = split[0].Trim();
            var tag = split[1].Trim();

            user = await _db.Users.FirstOrDefaultAsync(x =>
                x.Name.ToLower() == username.ToLower() && x.Tag.ToLower() == tag.ToLower());

            if (user is null)
                return (null, null, TaskResult.FromFailure("No account found with that username#tag.", errorCode: 404));

            privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == user.Id);
        }
        else if (long.TryParse(identifier, out var rawId))
        {
            user = await _db.Users.FindAsync(rawId);
            if (user is null)
                return (null, null, TaskResult.FromFailure("No account found with that id.", errorCode: 404));

            privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == rawId);
        }
        else
        {
            var username = identifier;
            var normalizedUsername = username.ToLower();
            var matches = await _db.Users
                .Where(x => x.Name.ToLower() == normalizedUsername)
                .OrderBy(x => x.Tag)
                .Select(x => new { x.Id, x.Name, x.Tag })
                .Take(5)
                .ToListAsync();

            if (matches.Count == 0)
                return (null, null, TaskResult.FromFailure("No account found with that username.", errorCode: 404));

            if (matches.Count > 1)
            {
                var matchList = string.Join(", ", matches.Select(x => $"{x.Name}#{x.Tag}"));
                return (null, null, TaskResult.FromFailure(
                    $"Multiple users match '{username}'. Retry with username#tag. Matches: {matchList}",
                    errorCode: 409));
            }

            var userId = matches[0].Id;
            user = await _db.Users.FindAsync(userId);
            privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == userId);
        }

        return (user, privateInfo, TaskResult.SuccessResult);
    }

    public async Task<TaskResult> VerifyUserByIdentifierAsync(string identifier)
    {
        var (user, privateInfo, error) = await ResolveUserByIdentifierAsync(identifier);
        if (user is null)
            return error;

        if (privateInfo is null)
            return TaskResult.FromFailure("Could not find complete user records for this account.", errorCode: 404);

        if (privateInfo.Verified)
            return TaskResult.FromSuccess($"{user.Name}#{user.Tag} is already verified.");

        privateInfo.Verified = true;

        // Clean up pending codes now that verification was done manually.
        _db.EmailConfirmCodes.RemoveRange(_db.EmailConfirmCodes.Where(x => x.UserId == user.Id));

        await _db.SaveChangesAsync();

        _logger.LogInformation("Manual staff verification completed for user {UserId} ({Name}#{Tag}).", user.Id, user.Name, user.Tag);

        return TaskResult.FromSuccess($"Verified {user.Name}#{user.Tag} ({privateInfo.Email}).");
    }

    ///////////////////
    // Staff tooling //
    ///////////////////

    /// <summary>
    /// How long a staff-scheduled MFA removal waits before executing. The
    /// delay plus the notice email is what makes this tool resistant to
    /// social engineering — the real account owner has time to object.
    /// </summary>
    public static readonly TimeSpan MfaRemovalDelay = TimeSpan.FromHours(48);

    /// <summary>
    /// PII lookup: resolves an identifier to a user + email for support.
    /// Always audit-logged with the staff-supplied reason.
    /// </summary>
    public async Task<TaskResult<StaffUserLookupResult>> LookupUserAsync(string identifier, long staffUserId, string reason)
    {
        var (user, privateInfo, error) = await ResolveUserByIdentifierAsync(identifier);
        if (user is null)
            return TaskResult<StaffUserLookupResult>.FromFailure(error.Message);

        var hasMfa = await _db.MultiAuths.AnyAsync(x => x.UserId == user.Id && x.Verified);
        var pendingRemoval = await _db.PendingMfaRemovals
            .Where(x => x.TargetUserId == user.Id && x.Status == MfaRemovalStatus.Pending)
            .Select(x => (DateTime?)x.ExecuteAt)
            .FirstOrDefaultAsync();

        await LogActionAsync(staffUserId, StaffActionType.LookupUser, user.Id, reason, $"identifier: {identifier}");

        return TaskResult<StaffUserLookupResult>.FromData(new StaffUserLookupResult
        {
            UserId = user.Id,
            Name = user.Name,
            Tag = user.Tag,
            Email = privateInfo?.Email,
            EmailVerified = privateInfo?.Verified ?? false,
            Disabled = user.Disabled,
            Bot = user.Bot,
            OwnerId = user.OwnerId,
            PriorName = user.PriorName,
            NameChangeTime = user.NameChangeTime,
            HidePriorName = user.HidePriorName,
            TimeCreated = user.TimeJoined,
            HasMfa = hasMfa,
            PendingMfaRemovalAt = pendingRemoval
        });
    }

    public async Task<TaskResult<List<User>>> GetOwnedBotsAsync(long userId, long staffUserId, string reason)
    {
        var owner = await _db.Users.FindAsync(userId);
        if (owner is null)
            return TaskResult<List<User>>.FromFailure("Account not found");

        var bots = await _db.Users
            .AsNoTracking()
            .Where(x => x.OwnerId == userId && x.Bot)
            .Select(x => x.ToModel())
            .ToListAsync();

        await LogActionAsync(staffUserId, StaffActionType.ViewOwnedBots, userId, reason, $"{bots.Count} bots");

        return TaskResult<List<User>>.FromData(bots);
    }

    /// <summary>
    /// Resets a username to a neutral auto-generated one. The old name is
    /// preserved as the prior name but hidden — resets are typically rule
    /// or privacy driven, so the old name should not be advertised.
    /// </summary>
    public async Task<TaskResult<string>> ResetUsernameAsync(long userId, long staffUserId, string reason)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult<string>.FromFailure("Account not found");

        var oldName = user.Name;

        string newName = null;
        for (var i = 0; i < 25 && newName is null; i++)
        {
            var candidate = $"Valournaut{Random.Shared.Next(100000, 1000000)}";
            var taken = await _db.Users.AnyAsync(x =>
                x.Name.ToLower() == candidate.ToLower() && x.Tag == user.Tag);
            if (!taken)
                newName = candidate;
        }

        if (newName is null)
            return TaskResult<string>.FromFailure("Could not generate a unique placeholder name. Try again.");

        user.Name = newName;
        user.PriorName = oldName;
        user.NameChangeTime = DateTime.UtcNow;
        user.HidePriorName = true;
        user.Version += 1;

        await _db.SaveChangesAsync();

        await LogActionAsync(staffUserId, StaffActionType.ResetUsername, userId, reason, $"{oldName} -> {newName}");

        return TaskResult<string>.FromData(newName);
    }

    public async Task<TaskResult> SetPriorNameHiddenAsync(long userId, bool hidden, long staffUserId, string reason)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult.FromFailure("Account not found", errorCode: 404);

        user.HidePriorName = hidden;
        user.Version += 1;

        await _db.SaveChangesAsync();

        await LogActionAsync(staffUserId,
            hidden ? StaffActionType.HidePriorName : StaffActionType.ShowPriorName,
            userId, reason);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Staff never see or set passwords. This sends the standard password
    /// reset email to the account's own address, optionally ending all
    /// active sessions (for suspected account compromise).
    /// </summary>
    public async Task<TaskResult> TriggerPasswordResetAsync(long userId, bool invalidateSessions, long staffUserId, string reason, HttpContext ctx)
    {
        var privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == userId);
        if (privateInfo is null)
            return TaskResult.FromFailure("Account not found", errorCode: 404);

        var sendResult = await _userService.SendPasswordResetEmail(privateInfo.ToModel(), privateInfo.Email, ctx);
        if (!sendResult.Success)
            return sendResult;

        if (invalidateSessions)
            await InvalidateSessionsAsync(userId);

        await LogActionAsync(staffUserId, StaffActionType.TriggerPasswordReset, userId, reason,
            invalidateSessions ? "sessions invalidated" : null);

        return TaskResult.FromSuccess("Password reset email sent to the account's address.");
    }

    /// <summary>
    /// Schedules MFA removal after a safety delay, emailing the account
    /// immediately so the real owner can object before it executes.
    /// </summary>
    public async Task<TaskResult> ScheduleMfaRemovalAsync(long userId, long staffUserId, string reason)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult.FromFailure("Account not found", errorCode: 404);

        var hasMfa = await _db.MultiAuths.AnyAsync(x => x.UserId == userId && x.Verified);
        if (!hasMfa)
            return TaskResult.FromFailure("This account has no verified multi-factor methods.");

        var existing = await _db.PendingMfaRemovals.AnyAsync(x =>
            x.TargetUserId == userId && x.Status == MfaRemovalStatus.Pending);
        if (existing)
            return TaskResult.FromFailure("An MFA removal is already pending for this account.");

        var executeAt = DateTime.UtcNow + MfaRemovalDelay;

        _db.PendingMfaRemovals.Add(new PendingMfaRemoval
        {
            Id = IdManager.Generate(),
            TargetUserId = userId,
            StaffUserId = staffUserId,
            Reason = reason ?? string.Empty,
            Status = MfaRemovalStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ExecuteAt = executeAt
        });

        await _db.SaveChangesAsync();

        var privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == userId);
        if (privateInfo?.Email is not null)
        {
            try
            {
                await EmailManager.SendEmailAsync(
                    privateInfo.Email,
                    "Valour: Two-factor authentication removal scheduled",
                    $"A Valour staff member has scheduled the removal of two-factor authentication from your account, following a support request. " +
                    $"It will take effect around {executeAt:u} (about {MfaRemovalDelay.TotalHours:0} hours from now).\n\n" +
                    "If you did NOT request this, someone may be trying to take over your account: " +
                    "log in and cancel the removal from Settings, change your password, and contact support@valour.gg immediately.\n\n" +
                    "If you did request it, no action is needed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send MFA removal notice to user {UserId}", userId);
            }
        }

        await LogActionAsync(staffUserId, StaffActionType.ScheduleMfaRemoval, userId, reason, $"executes at {executeAt:u}");

        return TaskResult.FromSuccess($"MFA removal scheduled. It executes in {MfaRemovalDelay.TotalHours:0} hours unless cancelled; the account has been emailed.");
    }

    /// <summary>
    /// Cancels a pending MFA removal. Callable by staff, or by the account
    /// owner themselves (actingUserId == target, isStaff false).
    /// </summary>
    public async Task<TaskResult> CancelMfaRemovalAsync(long userId, long actingUserId, bool isStaff, string reason)
    {
        var pending = await _db.PendingMfaRemovals.FirstOrDefaultAsync(x =>
            x.TargetUserId == userId && x.Status == MfaRemovalStatus.Pending);

        if (pending is null)
            return TaskResult.FromFailure("No pending MFA removal for this account.", errorCode: 404);

        pending.Status = MfaRemovalStatus.Cancelled;
        pending.ResolvedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        if (isStaff)
            await LogActionAsync(actingUserId, StaffActionType.CancelMfaRemoval, userId, reason);
        else
            _logger.LogInformation("User {UserId} cancelled their own pending MFA removal.", userId);

        return TaskResult.FromSuccess("Pending MFA removal cancelled.");
    }

    /// <summary>
    /// Executes due MFA removals. Called by the background worker.
    /// </summary>
    public async Task<int> ExecutePendingMfaRemovalsAsync()
    {
        var now = DateTime.UtcNow;
        var due = await _db.PendingMfaRemovals
            .Where(x => x.Status == MfaRemovalStatus.Pending && x.ExecuteAt <= now)
            .ToListAsync();

        foreach (var removal in due)
        {
            var methods = await _db.MultiAuths.Where(x => x.UserId == removal.TargetUserId).ToListAsync();
            _db.MultiAuths.RemoveRange(methods);

            removal.Status = MfaRemovalStatus.Executed;
            removal.ResolvedAt = now;

            await _db.SaveChangesAsync();

            await LogActionAsync(removal.StaffUserId, StaffActionType.ExecuteMfaRemoval, removal.TargetUserId,
                removal.Reason, "scheduled removal executed");

            var privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == removal.TargetUserId);
            if (privateInfo?.Email is not null)
            {
                try
                {
                    await EmailManager.SendEmailAsync(
                        privateInfo.Email,
                        "Valour: Two-factor authentication removed",
                        "The scheduled removal of two-factor authentication on your Valour account has completed. " +
                        "You can set up new multi-factor methods in Settings.\n\n" +
                        "If you did not expect this, change your password and contact support@valour.gg immediately.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send MFA removal completion notice to user {UserId}", removal.TargetUserId);
                }
            }
        }

        return due.Count;
    }
}
