using System.Security.Cryptography;
using Valour.Config.Configs;
using Valour.Server.Email;
using Valour.Shared.Cdn;
using Valour.Shared.Models;

namespace Valour.Server.Cdn;

/// <summary>
/// Responds when an upload matches known child sexual abuse material. The
/// upload is still rejected, but the file is kept in private storage and
/// quarantined so it is never served, as the law requires it to be preserved
/// for investigators. The uploader's account is disabled, staff receive a
/// report in the review queue, and an alert is emailed if one is configured.
/// Filing the report with NCMEC is left to staff, who confirm the match first.
/// </summary>
public class MediaSafetyIncidentService
{
    private readonly ValourDb _db;
    private readonly CdnBucketService _bucketService;
    private readonly StaffService _staffService;
    private readonly ReportService _reportService;
    private readonly ILogger<MediaSafetyIncidentService> _logger;

    public MediaSafetyIncidentService(
        ValourDb db,
        CdnBucketService bucketService,
        StaffService staffService,
        ReportService reportService,
        ILogger<MediaSafetyIncidentService> logger)
    {
        _db = db;
        _bucketService = bucketService;
        _staffService = staffService;
        _reportService = reportService;
        _logger = logger;
    }

    /// <summary>
    /// Handles a blocked upload. Each step runs even if an earlier one fails,
    /// so a storage error cannot stop the account from being disabled.
    /// </summary>
    public async Task HandleMatchAsync(
        long uploaderUserId,
        MemoryStream data,
        string fileName,
        string mimeType,
        MediaSafetyHashMatchResult match)
    {
        _logger.LogCritical(
            "Upload by user {UserId} matched known abuse material ({Provider} match {MatchId}). Preserving and disabling the account.",
            uploaderUserId, match.Provider, match.MatchId);

        var sha256 = Convert.ToHexString(SHA256.HashData(data.GetBuffer().AsSpan(0, (int)data.Length))).ToLowerInvariant();

        await RunStepAsync("preserve the file", () => PreserveAsync(uploaderUserId, data, fileName, mimeType, match));

        var otherUploaders = new List<long>();
        await RunStepAsync("quarantine other copies", async () =>
            otherUploaders = await QuarantineCopiesAsync(sha256, uploaderUserId, match));

        await RunStepAsync("disable the account", async () =>
        {
            var result = await _staffService.DisableUserAsync(uploaderUserId, true, ISharedUser.VictorUserId,
                $"Automatic: an upload matched known child sexual abuse material ({match.Provider} match {match.MatchId}).");
            if (!result.Success)
                throw new InvalidOperationException(result.Message);
        });

        var summary = BuildSummary(uploaderUserId, sha256, match, otherUploaders);

        await RunStepAsync("file a staff report", async () =>
        {
            var result = await _reportService.CreateAsync(new Report
            {
                ReportingUserId = ISharedUser.VictorUserId,
                ReportedUserId = uploaderUserId,
                ReasonCode = ReportReasonCode.IsMinorSexualContent,
                LongReason = summary,
            });
            if (!result.Success)
                throw new InvalidOperationException(result.Message);
        });

        var alertEmail = MediaSafetyConfig.Current?.AlertEmail;
        if (!string.IsNullOrWhiteSpace(alertEmail))
        {
            await RunStepAsync("email the alert", () => EmailManager.SendEmailAsync(alertEmail,
                "Valour: upload matched known child sexual abuse material", summary));
        }
    }

    private async Task PreserveAsync(long uploaderUserId, MemoryStream data, string fileName, string mimeType, MediaSafetyHashMatchResult match)
    {
        data.Position = 0;
        var result = await _bucketService.Upload(data, fileName, ExtensionFor(mimeType), uploaderUserId,
            mimeType, ContentCategory.Image, _db, match);

        // A file that is already quarantined is already preserved.
        if (!result.Success && result.Message != CdnBucketService.UnavailableMessage)
            throw new InvalidOperationException(result.Message);
    }

    /// <summary>
    /// Stops serving every stored copy of the file, including ones uploaded
    /// before scanning was enabled. Returns the other accounts that uploaded it.
    /// </summary>
    private async Task<List<long>> QuarantineCopiesAsync(string sha256, long uploaderUserId, MediaSafetyHashMatchResult match)
    {
        var copies = await _db.CdnBucketItems.IgnoreQueryFilters()
            .Where(x => x.Sha256Hash == sha256)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var copy in copies.Where(x => x.SafetyQuarantinedAt is null))
        {
            copy.SafetyQuarantinedAt = now;
            copy.SafetyHashMatchState = MediaSafetyHashMatchState.Matched;
            copy.SafetyProvider = match.Provider;
            copy.SafetyMatchId = match.MatchId;
            copy.SafetyHashMatchedAt = match.HashMatchedAt ?? now;
        }

        await _db.SaveChangesAsync();

        return copies.Select(x => x.UserId).Where(x => x != uploaderUserId).Distinct().ToList();
    }

    private static string BuildSummary(long uploaderUserId, string sha256, MediaSafetyHashMatchResult match, List<long> otherUploaders)
    {
        var lines = new List<string>
        {
            "An upload matched known child sexual abuse material. The upload was blocked, the file was preserved in quarantine, and the uploader's account was disabled.",
            "Confirm the match and file a CyberTipline report with NCMEC.",
            "",
            $"Uploader user ID: {uploaderUserId}",
            $"Provider: {match.Provider}",
            $"Match ID: {match.MatchId ?? "none returned"}",
            $"Matched at (UTC): {(match.HashMatchedAt ?? DateTime.UtcNow):u}",
            $"File SHA-256: {sha256}",
        };

        if (otherUploaders.Count > 0)
            lines.Add($"Other accounts that uploaded the same file (their copies are now quarantined): {string.Join(", ", otherUploaders)}");

        return string.Join("\n", lines);
    }

    private static string ExtensionFor(string mimeType) => mimeType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tiff",
        "image/webp" => ".webp",
        _ => "",
    };

    private async Task RunStepAsync(string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, "Media safety incident: failed to {Step}. Staff must do this by hand.", step);
        }
    }
}
