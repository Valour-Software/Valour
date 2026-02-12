using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Queries;

namespace Valour.Server.Services;

public class StaffService
{
    private readonly ValourDb _db;
    private readonly UserService _userService;
    private readonly ILogger<StaffService> _logger;
    
    public StaffService(ValourDb db, UserService userService, ILogger<StaffService> logger)
    {
        _db = db;
        _userService = userService;
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
    
    public async Task<TaskResult> DisableUserAsync(long userId, bool value)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult.FromFailure("Account not found", errorCode: 404);
        
        user.Disabled = value;
        
        await _db.SaveChangesAsync();
        
        // Also remove all tokens
        var tokens = _db.AuthTokens.Where(x => x.UserId == userId);
        _db.AuthTokens.RemoveRange(tokens);
        
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> DeleteUserAsync(long userId)
    {
        var user = await _db.Users.FindAsync(userId);

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

    public async Task<TaskResult> VerifyUserByIdentifierAsync(string identifier)
    {
        identifier = identifier?.Trim();
        if (string.IsNullOrWhiteSpace(identifier))
            return TaskResult.FromFailure("Please provide a username, username#tag, or email.", errorCode: 400);

        Valour.Database.User user = null;
        Valour.Database.UserPrivateInfo privateInfo = null;

        if (identifier.Contains('@'))
        {
            var sanitized = UserUtils.SanitizeEmail(identifier);
            var validEmail = UserUtils.TestEmail(sanitized);
            if (!validEmail.Success)
                return TaskResult.FromFailure("Invalid email format.", errorCode: 400);

            var normalizedEmail = validEmail.Data.ToLower();
            privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);
            if (privateInfo is null)
                return TaskResult.FromFailure("No account found with that email.", errorCode: 404);

            user = await _db.Users.FindAsync(privateInfo.UserId);
        }
        else if (identifier.Contains('#'))
        {
            var split = identifier.Split('#', 2);
            if (split.Length != 2 || string.IsNullOrWhiteSpace(split[0]) || string.IsNullOrWhiteSpace(split[1]))
                return TaskResult.FromFailure("Invalid format. Use username#tag.", errorCode: 400);

            var username = split[0].Trim();
            var tag = split[1].Trim().ToUpper();

            user = await _db.Users.FirstOrDefaultAsync(x =>
                x.Name.ToLower() == username.ToLower() && x.Tag == tag);

            if (user is null)
                return TaskResult.FromFailure("No account found with that username#tag.", errorCode: 404);

            privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == user.Id);
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
                return TaskResult.FromFailure("No account found with that username.", errorCode: 404);

            if (matches.Count > 1)
            {
                var matchList = string.Join(", ", matches.Select(x => $"{x.Name}#{x.Tag}"));
                return TaskResult.FromFailure(
                    $"Multiple users match '{username}'. Retry with username#tag. Matches: {matchList}",
                    errorCode: 409);
            }

            var userId = matches[0].Id;
            user = await _db.Users.FindAsync(userId);
            privateInfo = await _db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == userId);
        }

        if (user is null || privateInfo is null)
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
}
